// dolphin_rvz_wrapper.h
// Dolphin DolphinTool DLL wrapper for C# interop

#pragma once

#ifdef _WIN32
  #ifdef DOLPHIN_RVZ_EXPORTS
    #define DOLPHIN_RVZ_API __declspec(dllexport)
  #else
    #define DOLPHIN_RVZ_API __declspec(dllimport)
  #endif
#else
  #define DOLPHIN_RVZ_API __attribute__((visibility("default")))
#endif

extern "C"
{

// 진행률 콜백: false 반환 시 취소
typedef bool (__stdcall* ProgressCallback)(const char* text, float percent);

// 로그/에러 콜백
typedef void (__stdcall* LogCallback)(const char* message);

// ---------------------------------------------------------------------------
// ISO/RVZ/WIA/GCZ → RVZ
// compression: "zstd"(권장) / "none" / "bzip2" / "lzma" / "lzma2"
// compression_level: zstd=5 권장, 0이면 none
// block_size: 131072 (128KB) 권장
// ---------------------------------------------------------------------------
DOLPHIN_RVZ_API int rvz_convert_to_rvz(
    const char*      input,
    const char*      output,
    const char*      compression,
    int              compression_level,
    int              block_size,
    ProgressCallback progress,
    LogCallback      log);

// ---------------------------------------------------------------------------
// RVZ/WIA/GCZ → ISO
// ---------------------------------------------------------------------------
DOLPHIN_RVZ_API int rvz_convert_to_iso(
    const char* input,
    const char* output,
    const char* format,
    ProgressCallback progress,
    LogCallback      log);

// ---------------------------------------------------------------------------
// ISO/RVZ → GCZ
// block_size: 32768 (32KB) 권장
// ---------------------------------------------------------------------------
DOLPHIN_RVZ_API int rvz_convert_to_gcz(
    const char*      input,
    const char*      output,
    int              block_size,
    ProgressCallback progress,
    LogCallback      log);

// ---------------------------------------------------------------------------
// 반환값: 0=성공, -1=취소, 1=오류
// ---------------------------------------------------------------------------

} // extern "C"
