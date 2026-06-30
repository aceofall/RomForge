using System.Runtime.InteropServices;

namespace Patch.Core.Formats;

public static class Xdelta3
{
    private const string DllName = "xdelta3.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ProgressCallback(double progress);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int xd3_apply_patch_w(string sourcePath, string patchPath, string outputPath, IntPtr cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int xd3_apply_patch_w(string sourcePath, string patchPath, string outputPath, ProgressCallback cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int xd3_create_patch_w(string sourcePath, string newPath, string patchPath, IntPtr cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int xd3_create_patch_w(string sourcePath, string newPath, string patchPath, ProgressCallback cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr xd3_get_last_error();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int xd3_apply_patch_mem(byte[] sourceData, nuint sourceSize, byte[] patchData, nuint patchSize, out IntPtr outputData, out nuint outputSize, IntPtr cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int xd3_apply_patch_mem(byte[] sourceData, nuint sourceSize, byte[] patchData, nuint patchSize, out IntPtr outputData, out nuint outputSize, ProgressCallback cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void xd3_free_mem(IntPtr ptr);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void xd3_cancel();

    private static string GetLastError() => Marshal.PtrToStringAnsi(xd3_get_last_error()) ?? "unknown error";

    public static void ApplyPatch(string sourcePath, string patchPath, string outputPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateInputFiles(sourcePath, patchPath);

        using (cancellationToken.Register(() => xd3_cancel()))
        {
            if (onProgress is null)
            {
                ThrowIfFailed(xd3_apply_patch_w(sourcePath, patchPath, outputPath, IntPtr.Zero), "패치 적용");
                return;
            }

            ProgressCallback cb = progress => onProgress(progress);
            GCHandle handle = GCHandle.Alloc(cb);
            try
            {
                ThrowIfFailed(xd3_apply_patch_w(sourcePath, patchPath, outputPath, cb), "패치 적용");
            }
            finally
            {
                handle.Free();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public static byte[] ApplyPatch(byte[] sourceData, byte[] patchData, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        int ret;
        IntPtr outPtr;
        nuint outSize;

        using (cancellationToken.Register(() => xd3_cancel()))
        {
            if (onProgress is null)
                ret = xd3_apply_patch_mem(sourceData, (nuint)sourceData.Length, patchData, (nuint)patchData.Length, out outPtr, out outSize, IntPtr.Zero);
            else
            {
                ProgressCallback cb = progress => onProgress(Math.Min(progress, 1.0));
                GCHandle handle = GCHandle.Alloc(cb);
                try
                {
                    ret = xd3_apply_patch_mem(sourceData, (nuint)sourceData.Length, patchData, (nuint)patchData.Length, out outPtr, out outSize, cb);
                }
                finally
                {
                    handle.Free();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        ThrowIfFailed(ret, "패치 적용");

        try
        {
            var result = new byte[(int)outSize];
            Marshal.Copy(outPtr, result, 0, (int)outSize);
            return result;
        }
        finally
        {
            xd3_free_mem(outPtr);
        }
    }

    public static void CreatePatch(string sourcePath, string newPath, string patchPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateInputFiles(sourcePath, newPath);
        int result;

        using (cancellationToken.Register(() => xd3_cancel()))
        {
            if (onProgress is null)
                result = xd3_create_patch_w(sourcePath, newPath, patchPath, IntPtr.Zero);
            else
            {
                ProgressCallback cb = progress => onProgress(Math.Min(progress, 1.0));
                GCHandle handle = GCHandle.Alloc(cb);
                try
                {
                    result = xd3_create_patch_w(sourcePath, newPath, patchPath, cb);
                }
                finally
                {
                    handle.Free();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
        ThrowIfFailed(result, "패치 생성");
    }

    private static void ValidateInputFiles(params string[] paths)
    {
        foreach (var path in paths)
            if (!File.Exists(path))
                throw new FileNotFoundException($"파일을 찾을 수 없습니다: {path}");
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result == 0)
            return;

        int absResult = Math.Abs(result);

        string errorMessage = absResult switch
        {
            17710 => "내부 라이브러리 오류가 발생했습니다. (XD3_INTERNAL)",
            17711 => "잘못된 설정 값입니다. (XD3_INVALID)",
            17712 => "원본 파일이 패치 파일과 일치하지 않습니다. (미스매치 / XD3_INVALID_INPUT)",
            17713 => "보조 압축(Secondary Compression) 효율이 없어 적용할 수 없습니다. (XD3_NOSECOND)",
            17714 => "구현되지 않은 기능이 포함되어 있습니다. (XD3_UNIMPLEMENTED)",

            17703 => "입력 데이터가 더 필요합니다. (XD3_INPUT)",
            17704 => "출력 버퍼가 가득 찼습니다. (XD3_OUTPUT)",
            17705 => "소스 블록 데이터가 더 필요합니다. (XD3_GETSRCBLK)",

            2 => "지정된 파일 또는 경로를 찾을 수 없습니다. (ENOENT)",
            13 => "파일 접근 권한이 없습니다. (EACCES)",
            28 => "디스크 공간이 부족합니다. (ENOSPC)",

            _ => $"{GetLastError()} (Error Code: {result})"
        };

        throw new InvalidOperationException($"{errorMessage}");
    }
}