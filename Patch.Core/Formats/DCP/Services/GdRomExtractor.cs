using Patch.Core.Formats.DCP.Models;

namespace Patch.Core.Formats.DCP.Services;

public static class GdRomExtractor
{
    public static Iso9660Entry ExtractAll(GdiFile gdi, string outputDir, Action<string>? onFile = null)
    {
        using var reader = new GdRomCompositeSectorReader(gdi);
        var sectorFunc = reader.AsFunc();
        var root = Iso9660DirectoryReader.ReadTree(sectorFunc, reader.PvdAbsoluteLba);

        Directory.CreateDirectory(outputDir);

        foreach (var entry in Iso9660DirectoryReader.Flatten(root))
        {
            var outPath = Path.Combine(outputDir, entry.FullPath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            var data = Iso9660DirectoryReader.ReadFile(sectorFunc, entry);

            File.WriteAllBytes(outPath, data);

            onFile?.Invoke(entry.FullPath);
        }

        return root;
    }
}