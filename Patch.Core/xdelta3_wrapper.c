#ifdef _WIN32
#define _CRT_SECURE_NO_WARNINGS
#endif

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#define XD3_ENCODER         1
#define SECONDARY_LZMA      1
#define SECONDARY_DJW       1
#define XD3_USE_LARGEFILE64 1
#define XD3_STDIO           1
#define SIZEOF_SIZE_T       8
#define SIZEOF_USIZE_T      8
#define SIZEOF_XOFF_T       8

#include "xdelta3.h"

#ifdef _WIN32
#include <windows.h>
#include <io.h>
#define DLL_EXPORT __declspec(dllexport)
#define fseek64 _fseeki64
#define ftell64 _ftelli64
#else
#include <sys/mman.h>
#define DLL_EXPORT __attribute__((visibility("default")))
#define fseek64 fseeko
#define ftell64 ftello
#endif

typedef void (*progress_cb)(double);

#define INPUT_BUFSIZE (1 << 20)

static char last_error_msg[512] = { 0 };
static volatile int g_cancel_flag = 0;

DLL_EXPORT const char* xd3_get_last_error(void) {
    return last_error_msg;
}

DLL_EXPORT void xd3_cancel(void) {
    g_cancel_flag = 1;
}

static void save_error(xd3_stream* stream, int code) {
    if (stream && stream->msg)
        snprintf(last_error_msg, sizeof(last_error_msg),
            "%s (code: %d)", stream->msg, code);
    else
        snprintf(last_error_msg, sizeof(last_error_msg),
            "unknown error (code: %d)", code);
}

static void update_progress(long long current, long long total, progress_cb cb) {
    if (cb && total > 0) {
        double progress = (double)current / (double)total;
        if (progress > 1.0) progress = 1.0;
        cb(progress);
    }
}

typedef struct {
    uint8_t* buf;
    xoff_t   size;
#ifdef _WIN32
    HANDLE   hMap;
#endif
} src_ctx;

static int my_getblk(xd3_stream* stream, xd3_source* source, xoff_t blkno)
{
    src_ctx* ctx = (src_ctx*)source->ioh;
    source->curblk = ctx->buf;
    source->curblkno = 0;
    source->onblk = (usize_t)ctx->size;
    return 0;
}

static src_ctx* src_ctx_new(FILE* f, xd3_source* source, xd3_stream* stream)
{
    src_ctx* ctx = (src_ctx*)calloc(1, sizeof(src_ctx));
    if (!ctx) return NULL;

    fseek64(f, 0, SEEK_END);
    ctx->size = (xoff_t)ftell64(f);
    fseek64(f, 0, SEEK_SET);

#ifdef _WIN32
    HANDLE hFile = (HANDLE)_get_osfhandle(_fileno(f));
    ctx->hMap = CreateFileMapping(hFile, NULL, PAGE_READONLY, 0, 0, NULL);
    if (!ctx->hMap) { free(ctx); return NULL; }
    ctx->buf = (uint8_t*)MapViewOfFile(ctx->hMap, FILE_MAP_READ, 0, 0, 0);
    if (!ctx->buf) { CloseHandle(ctx->hMap); free(ctx); return NULL; }
#else
    ctx->buf = (uint8_t*)mmap(NULL, ctx->size, PROT_READ, MAP_PRIVATE, fileno(f), 0);
    if (ctx->buf == MAP_FAILED) { free(ctx); return NULL; }
#endif

    source->blksize = (usize_t)ctx->size;
    source->ioh = ctx;
    source->curblk = ctx->buf;
    source->curblkno = 0;
    source->onblk = (usize_t)ctx->size;
    source->max_winsize = (usize_t)ctx->size;

    xd3_set_source_and_size(stream, source, ctx->size);
    return ctx;
}

static void src_ctx_free(src_ctx* ctx) {
    if (!ctx) return;
#ifdef _WIN32
    if (ctx->buf) UnmapViewOfFile(ctx->buf);
    if (ctx->hMap) CloseHandle(ctx->hMap);
#else
    if (ctx->buf) munmap(ctx->buf, ctx->size);
#endif
    free(ctx);
}

static int run_xd3(xd3_stream* stream, FILE* input, FILE* output, long long total_size, long long src_size, progress_cb cb, int is_encode)
{
    uint8_t* inbuf = (uint8_t*)malloc(INPUT_BUFSIZE);
    if (!inbuf) return XD3_INTERNAL;

    long long done = 0;
    int ret = 0, eof = 0;

    while (!eof) {
        size_t n = fread(inbuf, 1, INPUT_BUFSIZE, input);
        if (n == 0) {
            if (feof(input)) {
                eof = 1;
                xd3_set_flags(stream, XD3_FLUSH | stream->flags);
            }
            else {
                ret = XD3_INTERNAL;
                break;
            }
        }
        xd3_avail_input(stream, inbuf, (usize_t)n);
        done += (long long)n;

    process:
        ret = is_encode ? xd3_encode_input(stream)
            : xd3_decode_input(stream);
        switch (ret) {
        case XD3_INPUT:
            break;

        case XD3_OUTPUT:
            if (fwrite(stream->next_out, 1, stream->avail_out, output)
                != stream->avail_out) {
                ret = XD3_INTERNAL;
                goto done;
            }
            xd3_consume_output(stream);
            goto process;

        case XD3_GETSRCBLK:
            ret = my_getblk(stream, stream->src, stream->src->getblkno);
            if (ret != 0) goto done;
            goto process;

        case XD3_GOTHEADER:
        case XD3_WINSTART:
            goto process;

        case XD3_WINFINISH:
            if (g_cancel_flag) {
                ret = -99;
                goto done;
            }
            if (is_encode) {
                update_progress(done, total_size, cb);
            }
            else {
                long long current_out = ftell64(output);
                update_progress(current_out, src_size, cb);
            }
            goto process;

        default:
            goto done;
        }
    }
done:
    free(inbuf);
    return ret;
}

#ifdef _WIN32
DLL_EXPORT int xd3_apply_patch_w(const wchar_t* source_path, const wchar_t* patch_path, const wchar_t* output_path, progress_cb cb)
{
    last_error_msg[0] = 0;
    g_cancel_flag = 0;
    FILE* src = _wfopen(source_path, L"rb");
    FILE* patch = _wfopen(patch_path, L"rb");
    FILE* out = _wfopen(output_path, L"wb");
#else
DLL_EXPORT int xd3_apply_patch(const char* source_path, const char* patch_path, const char* output_path, progress_cb cb)
{
    last_error_msg[0] = 0;
    g_cancel_flag = 0;
    FILE* src = fopen(source_path, "rb");
    FILE* patch = fopen(patch_path, "rb");
    FILE* out = fopen(output_path, "wb");
#endif

    if (!src || !patch || !out) {
        snprintf(last_error_msg, sizeof(last_error_msg),
            "fopen failed: src=%s patch=%s out=%s",
            src ? "ok" : "fail", patch ? "ok" : "fail", out ? "ok" : "fail");
        if (src) fclose(src); if (patch) fclose(patch); if (out) fclose(out);
        return -1;
    }

    xd3_stream stream;
    xd3_config config;
    xd3_source source;
    src_ctx* ctx = NULL;

    memset(&stream, 0, sizeof(stream));
    memset(&source, 0, sizeof(source));

    xd3_init_config(&config, 0);
    config.winsize = (1 << 26);
    config.getblk = my_getblk;

    int ret = xd3_config_stream(&stream, &config);
    if (ret != 0) { save_error(&stream, ret); goto fail; }

    fseek64(src, 0, SEEK_END);
    long long src_size = ftell64(src);
    fseek64(src, 0, SEEK_SET);

    fseek64(patch, 0, SEEK_END);
    long long patch_size = ftell64(patch);
    fseek64(patch, 0, SEEK_SET);

    ctx = src_ctx_new(src, &source, &stream);
    if (!ctx) {
        snprintf(last_error_msg, sizeof(last_error_msg), "src_ctx_new failed");
        ret = -2; goto fail;
    }

    ret = run_xd3(&stream, patch, out, patch_size, src_size, cb, 0);
    if (ret != 0 && ret != XD3_INPUT) save_error(&stream, ret);

fail:
    xd3_free_stream(&stream);
    src_ctx_free(ctx);
    fclose(src); fclose(patch); fclose(out);
    return (ret == 0 || ret == XD3_INPUT) ? 0 : ret;
}

#ifdef _WIN32
DLL_EXPORT int xd3_create_patch_w(const wchar_t* source_path, const wchar_t* new_path, const wchar_t* patch_path, progress_cb cb)
{
    last_error_msg[0] = 0;
    g_cancel_flag = 0;
    FILE* src = _wfopen(source_path, L"rb");
    FILE* newf = _wfopen(new_path, L"rb");
    FILE* patch = _wfopen(patch_path, L"wb");
#else
DLL_EXPORT int xd3_create_patch(const char* source_path, const char* new_path, const char* patch_path, progress_cb cb)
{
    last_error_msg[0] = 0;
    g_cancel_flag = 0;
    FILE* src = fopen(source_path, "rb");
    FILE* newf = fopen(new_path, "rb");
    FILE* patch = fopen(patch_path, "wb");
#endif

    if (!src || !newf || !patch) {
        snprintf(last_error_msg, sizeof(last_error_msg),
            "fopen failed: src=%s new=%s patch=%s",
            src ? "ok" : "fail", newf ? "ok" : "fail", patch ? "ok" : "fail");
        if (src) fclose(src); if (newf) fclose(newf); if (patch) fclose(patch);
        return -1;
    }

    fseek64(src, 0, SEEK_END);
    long long src_size = ftell64(src);
    fseek64(src, 0, SEEK_SET);

    fseek64(newf, 0, SEEK_END);
    long long new_size = ftell64(newf);
    fseek64(newf, 0, SEEK_SET);

    xd3_stream stream;
    xd3_config config;
    xd3_source source;
    src_ctx* ctx = NULL;

    memset(&stream, 0, sizeof(stream));
    memset(&source, 0, sizeof(source));

    xd3_init_config(&config, XD3_SEC_LZMA);
    config.winsize = (1 << 26);
    config.getblk = my_getblk;

    int ret = xd3_config_stream(&stream, &config);
    if (ret != 0) { save_error(&stream, ret); goto fail; }

    ctx = src_ctx_new(src, &source, &stream);
    if (!ctx) {
        snprintf(last_error_msg, sizeof(last_error_msg), "src_ctx_new failed");
        ret = -2; goto fail;
    }

    ret = run_xd3(&stream, newf, patch, new_size, src_size, cb, 1);
    if (ret != 0 && ret != XD3_INPUT) save_error(&stream, ret);

fail:
    xd3_free_stream(&stream);
    src_ctx_free(ctx);
    fclose(src); fclose(newf); fclose(patch);
    return (ret == 0 || ret == XD3_INPUT) ? 0 : ret;
}

DLL_EXPORT int xd3_apply_patch_mem(const uint8_t * source_data, size_t source_size, const uint8_t * patch_data, size_t patch_size, uint8_t * *output_data, size_t * output_size, progress_cb cb)
{
    last_error_msg[0] = 0;
    g_cancel_flag = 0;

    xd3_stream stream;
    xd3_config config;
    xd3_source source;

    memset(&stream, 0, sizeof(stream));
    memset(&source, 0, sizeof(source));

    xd3_init_config(&config, 0);
    config.winsize = (1 << 26);
    config.getblk = my_getblk;

    int ret = xd3_config_stream(&stream, &config);
    if (ret != 0) { save_error(&stream, ret); return ret; }

    src_ctx ctx = { (uint8_t*)source_data, (xoff_t)source_size };
    source.blksize = (usize_t)source_size;
    source.ioh = &ctx;
    source.curblk = source_data;
    source.curblkno = 0;
    source.onblk = (usize_t)source_size;
    source.max_winsize = (usize_t)source_size;
    xd3_set_source_and_size(&stream, &source, source_size);

    size_t out_cap = source_size + (source_size / 4) + (1 << 20);
    size_t out_len = 0;
    uint8_t* out_buf = (uint8_t*)malloc(out_cap);
    if (!out_buf) { xd3_free_stream(&stream); return XD3_INTERNAL; }

    const uint8_t* p = patch_data;
    size_t left = patch_size;
    size_t consumed = 0;
    int eof = 0;

    while (!eof) {
        size_t n = left > INPUT_BUFSIZE ? INPUT_BUFSIZE : left;
        if (n == 0) {
            eof = 1;
            xd3_set_flags(&stream, XD3_FLUSH | stream.flags);
            xd3_avail_input(&stream, p, 0);
        }
        else {
            xd3_avail_input(&stream, p, (usize_t)n);
            p += n;
            left -= n;
            consumed += n;
        }

    process:
        ret = xd3_decode_input(&stream);
        switch (ret) {
        case XD3_INPUT:
            break;

        case XD3_OUTPUT: {
            size_t need = out_len + stream.avail_out;
            if (need > out_cap) {
                out_cap = need * 2;
                uint8_t* tmp = (uint8_t*)realloc(out_buf, out_cap);
                if (!tmp) { free(out_buf); ret = XD3_INTERNAL; goto done; }
                out_buf = tmp;
            }
            memcpy(out_buf + out_len, stream.next_out, stream.avail_out);
            out_len += stream.avail_out;
            xd3_consume_output(&stream);
            goto process;
        }

        case XD3_GETSRCBLK:
            ret = my_getblk(&stream, stream.src, stream.src->getblkno);
            if (ret != 0) goto done;
            goto process;

        case XD3_GOTHEADER:
        case XD3_WINSTART:
            goto process;

        case XD3_WINFINISH:
            if (g_cancel_flag) { ret = -99; goto done; }
            update_progress(out_len, (long long)source_size, cb);
            goto process;

        default:
            goto done;
        }
    }

done:
    xd3_free_stream(&stream);
    if (ret == 0 || ret == XD3_INPUT) {
        *output_data = out_buf;
        *output_size = out_len;
        return 0;
    }
    free(out_buf);
    return ret;
}

DLL_EXPORT void xd3_free_mem(void* ptr) {
    free(ptr);
}