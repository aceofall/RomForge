using System.IO.Compression;

namespace Patch.Core.Formats.DCP.Services;

public static class DcpGdRomApplier
{
    public static async Task ApplyAsync(string gdiPath, string dcpPath, string outputDir, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        var gdi = GdiFile.Parse(gdiPath);

        using var sourceReader = new GdRomCompositeSectorReader(gdi);
        var sourceFunc = sourceReader.AsFunc();
        var pvdLba = sourceReader.PvdAbsoluteLba;
        var root = Iso9660DirectoryReader.ReadTree(sourceFunc, pvdLba);
        var byPath = Iso9660DirectoryReader.Flatten(root)
            .ToDictionary(e => e.FullPath.Replace('/', '\\'), e => e, StringComparer.OrdinalIgnoreCase);
        var replacedFiles = new Dictionary<string, byte[]>();
        using var archive = ZipFile.OpenRead(dcpPath);

        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        int entryDone = 0;

        foreach (var entry in entries)
        {
            var relativePath = entry.FullName.Replace('/', '\\');

            if (relativePath.StartsWith("bootsector\\", StringComparison.OrdinalIgnoreCase))
                continue;

            using var entryStream = entry.Open();
            using var ms = new MemoryStream();

            await entryStream.CopyToAsync(ms, ct);

            replacedFiles[relativePath] = ms.ToArray();

            entryDone++;
            onProgress?.Invoke(0.20 * entryDone / entries.Count);
        }

        var xdeltaEntries = replacedFiles.Where(x => x.Key.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase)).ToList();
        int xdeltaDone = 0;

        foreach (var kv in xdeltaEntries)
        {
            var targetPath = kv.Key[..^".xdelta".Length];

            if (!byPath.TryGetValue(targetPath, out var originalEntry))
                throw new InvalidOperationException($"DCP가 참조하는 원본 파일을 찾을 수 없습니다: {targetPath}");

            var originalData = Iso9660DirectoryReader.ReadFile(sourceFunc, originalEntry);

            var patched = await Task.Run(
                () => Xdelta3.ApplyPatch(originalData, kv.Value, null, ct),
                ct);

            replacedFiles[targetPath] = patched;
            replacedFiles.Remove(kv.Key);

            xdeltaDone++;
            onProgress?.Invoke(0.20 + 0.20 * xdeltaDone / xdeltaEntries.Count);
        }

        await Task.Run(() => GdRomRebuilder.RebuildFull(gdi, replacedFiles, outputDir, p => onProgress?.Invoke(0.40 + 0.60 * p), ct), ct);

        onProgress?.Invoke(1.0);
    }
}