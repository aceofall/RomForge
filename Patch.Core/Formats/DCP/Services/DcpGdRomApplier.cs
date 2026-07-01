using System.IO.Compression;

namespace Patch.Core.Formats.DCP.Services;

public static class DcpGdRomApplier
{
    public static async Task ApplyAsync(string gdiPath, string dcpPath, string outputDir, CancellationToken ct = default)
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

        foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            var relativePath = entry.FullName.Replace('/', '\\');

            if (relativePath.StartsWith("bootsector\\", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isXdelta = relativePath.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase);
            var targetPath = isXdelta ? relativePath[..^".xdelta".Length] : relativePath;
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();

            await entryStream.CopyToAsync(ms, ct);

            var entryData = ms.ToArray();

            if (isXdelta)
            {
                if (!byPath.TryGetValue(targetPath, out var originalEntry))
                    throw new InvalidOperationException($"DCP가 참조하는 원본 파일을 찾을 수 없습니다: {targetPath}");

                var originalData = Iso9660DirectoryReader.ReadFile(sourceFunc, originalEntry);
                var patched = await Task.Run(() => Xdelta3.ApplyPatch(originalData, entryData, null, ct), ct);

                replacedFiles[targetPath] = patched;
            }
            else
                replacedFiles[targetPath] = entryData;
        }

        GdRomRebuilder.RebuildFull(gdi, replacedFiles, outputDir);
    }
}