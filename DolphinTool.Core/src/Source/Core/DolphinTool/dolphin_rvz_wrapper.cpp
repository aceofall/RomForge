// dolphin_rvz_wrapper.cpp
// DolphinTool ConvertCommand DLL 래퍼
// ConvertCommand.cpp/h 패치 버전과 함께 사용

#define DOLPHIN_RVZ_EXPORTS
#include "dolphin_rvz_wrapper.h"

#include <atomic>
#include <string>
#include <vector>

#include "DolphinTool/ConvertCommand.h"

// ---------------------------------------------------------------------------
// 전역 상태
// ---------------------------------------------------------------------------
static ProgressCallback  g_progress_cb      = nullptr;
static LogCallback       g_log_cb           = nullptr;
static std::atomic<bool> g_cancel_requested = false;

// ---------------------------------------------------------------------------
// 내부 유틸
// ---------------------------------------------------------------------------
static int run_convert(std::vector<std::string> args,
                       ProgressCallback progress,
                       LogCallback      log)
{
    g_cancel_requested = false;
    g_progress_cb      = progress;
    g_log_cb           = log;

    // ConvertCommand.cpp의 전역 콜백에 주입
    DolphinTool::g_status_callback = [](const std::string& text, float percent) -> bool
    {
        if (g_cancel_requested)
            return false;
        if (g_progress_cb)
            return g_progress_cb(text.c_str(), percent);
        return true;
    };

    int result = EXIT_FAILURE;
    try
    {
        result = DolphinTool::ConvertCommand(args);
    }
    catch (...) {}

    // 사용 후 정리
    DolphinTool::g_status_callback = nullptr;

    if (result == EXIT_SUCCESS) return 0;
    return g_cancel_requested ? -1 : 1;
}

// ---------------------------------------------------------------------------
// export 함수
// ---------------------------------------------------------------------------
extern "C" DOLPHIN_RVZ_API int rvz_convert_to_rvz(
    const char*      input,
    const char*      output,
    const char*      compression,
    int              compression_level,
    int              block_size,
    ProgressCallback progress,
    LogCallback      log)
{
    return run_convert({
        "--input",             input,
        "--output",            output,
        "--format",            "rvz",
        "--compression",       compression,
        "--compression_level", std::to_string(compression_level),
        "--block_size",        std::to_string(block_size),
    }, progress, log);
}

extern "C" DOLPHIN_RVZ_API int rvz_convert_to_iso(
    const char* input,
    const char* output,
    const char* format,
    ProgressCallback progress,
    LogCallback      log)
{
    const char* target_format = (format != nullptr) ? format : "iso";

    return run_convert({
        "--input",  input,
        "--output", output,
        "--format", target_format,
    }, progress, log);
}

extern "C" DOLPHIN_RVZ_API int rvz_convert_to_gcz(
    const char*      input,
    const char*      output,
    int              block_size,
    ProgressCallback progress,
    LogCallback      log)
{
    return run_convert({
        "--input",      input,
        "--output",     output,
        "--format",     "gcz",
        "--block_size", std::to_string(block_size),
    }, progress, log);
}

extern "C" DOLPHIN_RVZ_API void rvz_cancel()
{
    g_cancel_requested = true;
}
