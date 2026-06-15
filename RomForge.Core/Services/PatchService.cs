using Common;
using Patch.Core;
using System.IO;
using System.IO.Compression;

namespace RomForge.Core.Services;

public static class PatchService
{
    public static async Task ApplyAsync(string sourcePath, string patchPath, string? outputFolder = null, IProgress<ProgressInfo>? progress = null, Action<string, LogLevel>? log = null, CancellationToken ct = default)
    {
        bool sourceIsZipEntry = sourcePath.Contains('|');
        bool patchIsZipEntry = patchPath.Contains('|');
        var sourceBytes = sourceIsZipEntry ? await ReadZipEntryAsync(sourcePath) : await File.ReadAllBytesAsync(sourcePath, ct);
        var patchBytes = patchIsZipEntry ? await ReadZipEntryAsync(patchPath) : await File.ReadAllBytesAsync(patchPath, ct);

        ct.ThrowIfCancellationRequested();

        var result = await Task.Run(() => UniversalPatcher.ApplyPatch(sourceBytes, patchBytes, p => progress?.Report(new ProgressInfo { Percent = (int)(p * 100) })), ct);
        string outputPath = ResolveNormalOutputPath(sourcePath, patchPath, outputFolder);

        if (sourceIsZipEntry)
            await WriteZipEntryAsync(sourcePath, result, outputPath);
        else
            await File.WriteAllBytesAsync(outputPath, result, ct);

        log?.Invoke($"[{Path.GetFileName(outputPath)}] 완료", LogLevel.Ok);
    }

    public static string ResolveNormalOutputPath(string sourcePath, string patchPath, string? outputFolder = null)
    {
        string realSourcePath = sourcePath.Contains('|') ? sourcePath.Split('|')[0] : sourcePath;

        string dir = string.IsNullOrEmpty(outputFolder)
            ? Path.GetDirectoryName(Path.GetFullPath(realSourcePath))!
            : outputFolder;

        if (sourcePath.Contains('|'))
        {
            string zipName = Path.GetFileNameWithoutExtension(realSourcePath) + "_patched.zip";

            return Path.Combine(dir, zipName);
        }

        string ext = Path.GetExtension(realSourcePath);
        string name = Path.GetFileNameWithoutExtension(realSourcePath) + "_patched" + ext;

        return Path.Combine(dir, name);
    }

    private static async Task<byte[]> ReadZipEntryAsync(string virtualPath)
    {
        var parts = virtualPath.Split('|', 2);
        string zipPath = parts[0];
        string entry = parts[1];
        using var zip = ZipFile.OpenRead(zipPath);
        var e = zip.GetEntry(entry) ?? throw new FileNotFoundException($"ZIP 엔트리 없음: {entry}");
        using var stream = e.Open();
        using var ms = new MemoryStream();

        await stream.CopyToAsync(ms);

        return ms.ToArray();
    }

    private static async Task WriteZipEntryAsync(string sourceVirtual, byte[] data, string outputZipPath)
    {
        var parts = sourceVirtual.Split('|', 2);
        string srcZip = parts[0];
        string entryName = parts[1];

        if (!File.Exists(outputZipPath))
            File.Copy(srcZip, outputZipPath);

        using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Update);
        var existing = zip.GetEntry(entryName);

        existing?.Delete();

        var newEntry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = newEntry.Open();

        await stream.WriteAsync(data);
    }
}