using Common;
using System.IO.Compression;

namespace Patch.Core.Services;

public static class PatchService
{
    /// <summary>
    /// 단건 패치 적용. sourcePath/patchPath 는 실제경로 or "zipPath|entryName" 가상경로.
    /// </summary>
    public static async Task ApplyAsync(
        string sourcePath,
        string patchPath,
        PatchConfig config,
        IProgress<ProgressInfo> progress,
        Action<string, LogLevel> log,
        CancellationToken ct)
    {
        bool sourceIsZipEntry = sourcePath.Contains('|');
        bool patchIsZipEntry  = patchPath.Contains('|');

        // 소스 추출
        byte[] sourceBytes = sourceIsZipEntry
            ? await ReadZipEntryAsync(sourcePath)
            : await File.ReadAllBytesAsync(sourcePath, ct);

        // 패치 추출
        byte[] patchBytes = patchIsZipEntry
            ? await ReadZipEntryAsync(patchPath)
            : await File.ReadAllBytesAsync(patchPath, ct);

        ct.ThrowIfCancellationRequested();

        // 패치 적용
        byte[] result = await Task.Run(() =>
            UniversalPatcher.Apply(sourceBytes, patchBytes, p =>
                progress.Report(new ProgressInfo { Percent = (int)(p * 100) })), ct);

        // 출력 경로 결정
        string outputPath = ResolveOutputPath(sourcePath, patchPath, config);

        // 백업 처리 (아케이드 모드)
        if (config.OutputMode == OutputMode.Arcade && File.Exists(outputPath))
            File.Move(outputPath, outputPath + ".bak", overwrite: true);

        // 원본이 ZIP 엔트리였으면 ZIP에 다시 쓰기
        if (sourceIsZipEntry)
            await WriteZipEntryAsync(sourcePath, result, outputPath, config);
        else
            await File.WriteAllBytesAsync(outputPath, result, ct);

        log($"[{Path.GetFileName(outputPath)}] 완료", LogLevel.Ok);
    }

    private static string ResolveOutputPath(string sourcePath, string patchPath, PatchConfig config)
    {
        string realSourcePath = sourcePath.Contains('|') ? sourcePath.Split('|')[0] : sourcePath;
        string dir = config.OutputFolder ?? Path.GetDirectoryName(realSourcePath)!;

        if (config.OutputMode == OutputMode.Arcade)
            return Path.Combine(dir, Path.GetFileName(realSourcePath));

        // ZIP 엔트리면 출력도 ZIP
        if (sourcePath.Contains('|'))
        {
            string zipName = Path.GetFileNameWithoutExtension(realSourcePath) + "_patched.zip";
            return Path.Combine(dir, zipName);
        }

        string ext  = Path.GetExtension(realSourcePath);
        string name = Path.GetFileNameWithoutExtension(realSourcePath) + "_patched" + ext;
        return Path.Combine(dir, name);
    }

    private static async Task<byte[]> ReadZipEntryAsync(string virtualPath)
    {
        var parts     = virtualPath.Split('|', 2);
        string zipPath = parts[0];
        string entry   = parts[1];

        using var zip  = ZipFile.OpenRead(zipPath);
        var e = zip.GetEntry(entry) ?? throw new FileNotFoundException($"ZIP 엔트리 없음: {entry}");
        using var stream = e.Open();
        using var ms     = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static async Task WriteZipEntryAsync(string sourceVirtual, byte[] data, string outputZipPath, PatchConfig config)
    {
        var parts      = sourceVirtual.Split('|', 2);
        string srcZip  = parts[0];
        string entryName = parts[1];

        // 원본 ZIP 복사 후 해당 엔트리만 교체
        if (!File.Exists(outputZipPath))
            File.Copy(srcZip, outputZipPath);

        using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Update);
        var existing  = zip.GetEntry(entryName);
        existing?.Delete();

        var newEntry  = zip.CreateEntry(entryName, CompressionLevel.Optimal); // Deflate
        using var stream = newEntry.Open();
        await stream.WriteAsync(data);
    }
}
