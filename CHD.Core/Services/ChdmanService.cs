using CHD.Core.Models;
using CHD.Core.Models.Enums;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CHD.Core.Services;

public sealed class ChdmanService : IDisposable
{
    private const string CHDMAN_DLL = "chdman.dll";

    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly ProgressCallback _progressCallback;
    private readonly LogCallback _logCallback;

    private int _lastProgress;
    private bool _disposed;

    #region DllImport

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProgressCallback(int percent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LogCallback([MarshalAs(UnmanagedType.LPStr)] string message);

    [DllImport(CHDMAN_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int chdman_create_cd([MarshalAs(UnmanagedType.LPUTF8Str)] string input, [MarshalAs(UnmanagedType.LPUTF8Str)] string output, ProgressCallback progress, LogCallback log);

    [DllImport(CHDMAN_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int chdman_create_dvd([MarshalAs(UnmanagedType.LPUTF8Str)] string input, [MarshalAs(UnmanagedType.LPUTF8Str)] string output, [MarshalAs(UnmanagedType.LPUTF8Str)] string compression, ProgressCallback progress, LogCallback log);

    [DllImport(CHDMAN_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int chdman_extract_cd([MarshalAs(UnmanagedType.LPUTF8Str)] string input, [MarshalAs(UnmanagedType.LPUTF8Str)] string output, ProgressCallback progress, LogCallback log);

    [DllImport(CHDMAN_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int chdman_extract_raw([MarshalAs(UnmanagedType.LPUTF8Str)] string input, [MarshalAs(UnmanagedType.LPUTF8Str)] string output, ProgressCallback progress, LogCallback log);

    [DllImport(CHDMAN_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int chdman_get_info([MarshalAs(UnmanagedType.LPUTF8Str)] string input, LogCallback log);

    [DllImport(CHDMAN_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void chdman_cancel();

    #endregion

    public event EventHandler<ProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? ErrorReceived;

    public ChdmanService()
    {
        _progressCallback = percent =>
        {
            if (percent >= _lastProgress)
            {
                _lastProgress = percent;
                ProgressChanged?.Invoke(this, new ProgressEventArgs(percent));
            }
        };
        _logCallback = msg => { if (!string.IsNullOrEmpty(msg)) ErrorReceived?.Invoke(this, msg); };
    }

    public Task<bool> CreateCdAsync(string cuePath, string chdPath, CancellationToken cancellationToken = default)
    {
        cuePath = Path.GetFullPath(cuePath);
        chdPath = Path.GetFullPath(chdPath);

        return RunLockedAsync(
            workingDir: Path.GetDirectoryName(cuePath)!,
            input: Path.GetFileName(cuePath),
            output: chdPath,
            invoke: chdman_create_cd,
            cancellationToken: cancellationToken);
    }

    public Task<bool> CreateDvdAsync(string isoPath, string chdPath, string compression = "zlib", CancellationToken cancellationToken = default)
    {
        isoPath = Path.GetFullPath(isoPath);
        chdPath = Path.GetFullPath(chdPath);

        return RunLockedAsync(
            workingDir: Path.GetDirectoryName(isoPath) ?? Path.GetPathRoot(isoPath)!,
            input: isoPath,
            output: chdPath,
            invoke: (i, o, p, l) => chdman_create_dvd(i, o, compression, p, l),
            cancellationToken: cancellationToken);
    }

    public Task<bool> ExtractCdAsync(string chdPath, string cuePath, CancellationToken cancellationToken = default)
    {
        chdPath = Path.GetFullPath(chdPath);
        cuePath = Path.GetFullPath(cuePath);

        return RunLockedAsync(
            workingDir: Path.GetDirectoryName(cuePath)!,
            input: chdPath,
            output: cuePath,
            invoke: chdman_extract_cd,
            cancellationToken: cancellationToken);
    }

    public Task<bool> ExtractRawAsync(string chdPath, string isoPath, CancellationToken cancellationToken = default)
    {
        chdPath = Path.GetFullPath(chdPath);
        isoPath = Path.GetFullPath(isoPath);

        return RunLockedAsync(
            workingDir: Path.GetDirectoryName(isoPath)!,
            input: chdPath,
            output: isoPath,
            invoke: chdman_extract_raw,
            cancellationToken: cancellationToken);
    }

    private async Task<bool> RunLockedAsync(string workingDir, string input, string output, Func<string, string, ProgressCallback, LogCallback, int> invoke, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cancelReg = cancellationToken.Register(static () => chdman_cancel());

            return await Task.Run(() => RunWithCwd(workingDir, input, output, invoke, cancelReg, cancellationToken),
                                  cancellationToken)
                             .ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool RunWithCwd(string workingDir, string input, string output, Func<string, string, ProgressCallback, LogCallback, int> invoke, CancellationTokenRegistration cancelReg, CancellationToken cancellationToken)
    {
        string originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(workingDir);
            int result = invoke(input, output, _progressCallback, _logCallback);

            cancelReg.Dispose();

            if (cancellationToken.IsCancellationRequested || result == -1)
                throw new OperationCanceledException(cancellationToken);

            return result == 0;
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    public static ChdmanInfo GetChdInfo(string chdPath)
    {
        chdPath = Path.GetFullPath(chdPath);

        if (!File.Exists(chdPath))
            throw new FileNotFoundException("CHD 파일을 찾을 수 없습니다.", chdPath);

        try
        {
            var info = ChdInfoReader.ReadChdInfo(chdPath);
            var fileInfo = new FileInfo(chdPath);
            long originalSize = CalculateOriginalSize(info);

            string ratio = originalSize > 0
                ? $"{(double)fileInfo.Length / originalSize * 100:F1}%"
                : "0%";

            return new ChdmanInfo
            {
                FileVersion = $"{info.Version}",
                LogicalSize = originalSize.ToString("N0"),
                ChdSize = fileInfo.Length.ToString("N0"),
                Ratio = ratio,
                Compression = info.GetCompressionInfo()
            };
        }
        catch
        {
            return GetChdInfoViaChdman(chdPath);
        }
    }

    private static ChdmanInfo GetChdInfoViaChdman(string chdPath)
    {
        var sb = new StringBuilder();

        LogCallback logDelegate = msg => sb.AppendLine(msg);
        int result = chdman_get_info(chdPath, logDelegate);
        GC.KeepAlive(logDelegate);

        if (result != 0)
            throw new InvalidOperationException($"chdman info 실패 (code={result})");

        return ParseChdInfo(sb.ToString())
            ?? throw new InvalidOperationException("CHD 정보 파싱 실패");
    }

    public static long CalculateOriginalSize(ChdInfo info)
    {
        if (info.SourceType is ChdSourceType.ISO or ChdSourceType.BinCue)
        {
            if (info.Tracks?.Length > 0)
            {
                return info.Tracks.Sum(track =>
                {
                    string t = track.TrackType?.ToUpperInvariant() ?? string.Empty;

                    int sectorSize = t switch
                    {
                        _ when t.Contains("AUDIO") => 2352,
                        _ when t.Contains("MODE1_RAW") => 2352,
                        _ when t.Contains("MODE1") => 2048,
                        _ when t.Contains("MODE2_RAW") => 2352,
                        _ when t.Contains("MODE2_FORM1") => 2048,
                        _ when t.Contains("MODE2_FORM2") => 2324,
                        _ when t.Contains("MODE2") => 2352,
                        _ => 2048
                    };

                    return (long)track.Frames * sectorSize;
                });
            }

            return (long)info.LogicalBytes;
        }

        return (long)info.LogicalBytes;
    }

    private static ChdmanInfo? ParseChdInfo(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        static string Match(string text, string pattern) =>
            Regex.Match(text, pattern).Groups[1].Value.Trim();

        var version = Match(output, @"File Version:\s*(.+)");
        var logicalSize = Match(output, @"Logical size:\s*([\d,]+)");
        var chdSize = Match(output, @"CHD size:\s*([\d,]+)");
        var ratio = Match(output, @"Ratio:\s*(.+)");

        if (string.IsNullOrEmpty(version) && string.IsNullOrEmpty(logicalSize))
            return null;

        return new ChdmanInfo
        {
            FileVersion = version,
            LogicalSize = logicalSize,
            ChdSize = chdSize,
            Ratio = ratio
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}