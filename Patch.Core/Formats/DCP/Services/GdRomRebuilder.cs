using Patch.Core.Formats.DCP.Models;

namespace Patch.Core.Formats.DCP.Services;

public static class GdRomRebuilder
{
    public static void RebuildFull(GdiFile originalGdi, Dictionary<string, byte[]> replacedFiles, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        using var sourceReader = new GdRomCompositeSectorReader(originalGdi);
        var sourceFunc = sourceReader.AsFunc();
        var firstDataTrack = originalGdi.Tracks
            .Where(t => t.Type == TrackType.Data && t.StartLba >= 45000)
            .OrderBy(t => t.StartLba)
            .First();
        var ipBinSectors = new byte[16][];

        for (uint s = 0; s < 16; s++)
            ipBinSectors[s] = sourceFunc((uint)firstDataTrack.StartLba + s);

        var originalPvdLba = sourceReader.PvdAbsoluteLba;
        var pvdRaw = sourceFunc(originalPvdLba);
        var root = Iso9660DirectoryReader.ReadTree(sourceFunc, originalPvdLba);
        var builder = new Iso9660Builder(pvdRaw, root);

        foreach (var entry in Iso9660DirectoryReader.Flatten(root))
        {
            var data = replacedFiles.TryGetValue(entry.FullPath, out var patched) ? patched : Iso9660DirectoryReader.ReadFile(sourceFunc, entry);

            builder.SetFileData(entry, data);
        }

        var (_, _, totalSectors) = builder.Relayout((uint)firstDataTrack.StartLba + 17);
        var contentSectors = new List<(uint Lba, byte[] Data)>();

        void CollectDirRecords(Iso9660Entry dir)
        {
            contentSectors.Add((dir.LayoutLba, Iso9660Builder.BuildDirectoryRecordData(dir)));

            foreach (var child in dir.Children.Where(c => !c.IsDirectory))
            {
                var data = replacedFiles.TryGetValue(child.FullPath, out var patched) ? patched : Iso9660DirectoryReader.ReadFile(sourceFunc, child);

                contentSectors.Add((child.LayoutLba, PadToSector(data)));
            }

            foreach (var child in dir.Children.Where(c => c.IsDirectory))
                CollectDirRecords(child);
        }

        CollectDirRecords(root);

        var newPvd = builder.BuildPvd(totalSectors);

        contentSectors.Add(((uint)firstDataTrack.StartLba + 16, PadToSector(newPvd)));

        var expanded = new List<(uint Lba, byte[] Sector2048)>();

        for (uint s = 0; s < 16; s++)
            expanded.Add(((uint)firstDataTrack.StartLba + s, ipBinSectors[s]));

        foreach (var (lba, data) in contentSectors)
        {
            int sectorCount = (int)Math.Ceiling(data.Length / 2048.0);

            for (int s = 0; s < sectorCount; s++)
            {
                var chunk = new byte[2048];
                int copyLen = Math.Min(2048, data.Length - s * 2048);

                Buffer.BlockCopy(data, s * 2048, chunk, 0, copyLen);
                expanded.Add(((uint)(lba + s), chunk));
            }
        }

        var dedup = expanded.GroupBy(e => e.Lba).Select(g => g.Last()).OrderBy(e => e.Lba).ToList();
        var newDataTrackPath = Path.Combine(outputDir, firstDataTrack.FileName);

        GdRomWriter.WriteDataTrack(newDataTrackPath, (uint)firstDataTrack.StartLba, dedup);

        foreach (var track in originalGdi.Tracks.Where(t => t != firstDataTrack))
        {
            var srcPath = originalGdi.GetTrackFullPath(track);
            var dstPath = Path.Combine(outputDir, track.FileName);

            File.Copy(srcPath, dstPath, overwrite: true);
        }

        WriteGdi(originalGdi, Path.Combine(outputDir, Path.GetFileName(originalGdi.GdiPath)));
    }

    private static byte[] PadToSector(byte[] data)
    {
        int sectors = (int)Math.Ceiling(data.Length / 2048.0);

        if (data.Length == sectors * 2048) 
            return data;

        var padded = new byte[sectors * 2048];

        Buffer.BlockCopy(data, 0, padded, 0, data.Length);

        return padded;
    }

    private static void WriteGdi(GdiFile original, string outputPath)
    {
        using var writer = new StreamWriter(outputPath);

        writer.WriteLine(original.Tracks.Count);

        foreach (var t in original.Tracks)
            writer.WriteLine($"{t.Number} {t.StartLba} {(int)t.Type} {t.SectorSize} \"{t.FileName}\" {t.FileOffset}");
    }
}