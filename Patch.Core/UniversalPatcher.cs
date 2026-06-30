using Patch.Core.Enums;
using Patch.Core.Formats;

namespace Patch.Core;

public static class UniversalPatcher
{
    public const long MemoryThreshold = 2L * 1024 * 1024 * 1024;

    public static async Task ApplyPatchAsync(string sourcePath, string patchPath, string outputPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException($"원본 파일을 찾을 수 없습니다: {sourcePath}");
        if (!File.Exists(patchPath)) throw new FileNotFoundException($"패치 파일을 찾을 수 없습니다: {patchPath}");

        cancellationToken.ThrowIfCancellationRequested();

        var sourceLength = new FileInfo(sourcePath).Length;
        var patchLength = new FileInfo(patchPath).Length;

        if (sourceLength < MemoryThreshold && patchLength < MemoryThreshold)
        {
            byte[] sourceData = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
            byte[] patchData = await File.ReadAllBytesAsync(patchPath, cancellationToken);
            await File.WriteAllBytesAsync(outputPath, await ApplyPatchAsync(sourceData, patchData, onProgress, cancellationToken), cancellationToken);
        }
        else
        {
            PatchFormat format = await DetectFormatAsync(patchPath, cancellationToken);

            switch (format)
            {
                case PatchFormat.Xdelta: await Task.Run(() => Xdelta3.ApplyPatch(sourcePath, patchPath, outputPath, onProgress, cancellationToken), cancellationToken); break;
                case PatchFormat.Ips: await Ips.ApplyPatchAsync(sourcePath, patchPath, outputPath, onProgress, cancellationToken); break;
                case PatchFormat.Bps: await Bps.ApplyPatchAsync(sourcePath, patchPath, outputPath, onProgress, cancellationToken); break;
                case PatchFormat.Ups: await Ups.ApplyPatchAsync(sourcePath, patchPath, outputPath, onProgress, cancellationToken); break;
                case PatchFormat.Ppf: await Ppf.ApplyPatchAsync(sourcePath, patchPath, outputPath, onProgress, cancellationToken); break;
                case PatchFormat.Aps: await Aps.ApplyPatchAsync(sourcePath, patchPath, outputPath, onProgress, cancellationToken); break;
                default: throw new NotSupportedException("지원되지 않거나 유효하지 않은 패치 포맷입니다.");
            }
        }
    }

    public static async Task<byte[]> ApplyPatchAsync(string sourcePath, string patchPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException($"원본 파일을 찾을 수 없습니다: {sourcePath}");
        if (!File.Exists(patchPath)) throw new FileNotFoundException($"패치 파일을 찾을 수 없습니다: {patchPath}");

        var sourceLength = new FileInfo(sourcePath).Length;
        var patchLength = new FileInfo(patchPath).Length;

        if (sourceLength >= MemoryThreshold || patchLength >= MemoryThreshold)
            throw new InvalidOperationException("2GB 이상 파일은 ApplyPatch(string, string, string)을 사용하세요.");

        cancellationToken.ThrowIfCancellationRequested();

        byte[] sourceData = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        byte[] patchData = await File.ReadAllBytesAsync(patchPath, cancellationToken);

        return await ApplyPatchAsync(sourceData, patchData, onProgress, cancellationToken);
    }

    public static async Task<byte[]> ApplyPatchAsync(byte[] sourceData, byte[] patchData, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        PatchFormat format = DetectFormat(patchData);

        return format switch
        {
            PatchFormat.Xdelta => await Task.Run(() => Xdelta3.ApplyPatch(sourceData, patchData, onProgress, cancellationToken), cancellationToken),
            PatchFormat.Ips => await Ips.ApplyPatchAsync(sourceData, patchData, onProgress, cancellationToken),
            PatchFormat.Bps => await Bps.ApplyPatchAsync(sourceData, patchData, onProgress, cancellationToken),
            PatchFormat.Ups => await Ups.ApplyPatchAsync(sourceData, patchData, onProgress, cancellationToken),
            PatchFormat.Ppf => await Ppf.ApplyPatchAsync(sourceData, patchData, onProgress, cancellationToken),
            PatchFormat.Aps => await Aps.ApplyPatchAsync(sourceData, patchData, onProgress, cancellationToken),
            _ => throw new NotSupportedException("지원되지 않거나 유효하지 않은 패치 포맷입니다.")
        };
    }

    public static async Task<PatchFormat> DetectFormatAsync(string patchPath, CancellationToken ct = default)
    {
        if (!File.Exists(patchPath))
            throw new FileNotFoundException($"파일을 찾을 수 없습니다: {patchPath}");

        byte[] header = new byte[8];

        using var fs = new FileStream(patchPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        int read = await fs.ReadAsync(header, ct);

        return DetectFormat(header.AsSpan(0, read));
    }

    public static PatchFormat DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return PatchFormat.Unknown;

        if (data.Length >= 3 && data[0] == 0xD6 && data[1] == 0xC3 && data[2] == 0xC4)
            return PatchFormat.Xdelta;

        if (data.Length >= 5 && data[0] == 'P' && data[1] == 'A' && data[2] == 'T' && data[3] == 'C' && data[4] == 'H')
            return PatchFormat.Ips;

        if (data[0] == 'B' && data[1] == 'P' && data[2] == 'S' && data[3] == '1')
            return PatchFormat.Bps;

        if (data[0] == 'U' && data[1] == 'P' && data[2] == 'S' && data[3] == '1')
            return PatchFormat.Ups;

        if (data.Length >= 3 && data[0] == 'P' && data[1] == 'P' && data[2] == 'F')
            return PatchFormat.Ppf;

        if (data[0] == 'A' && data[1] == 'P' && data[2] == 'S' && data[3] == '1')
            return PatchFormat.Aps;

        return PatchFormat.Unknown;
    }
}