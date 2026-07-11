using Common;
using RomForge.Core.Models.WiiU;
using System.Diagnostics;
using System.IO;
using WiiU.Core.Models;
using WiiU.Core.Services;

namespace RomForge.Core.Services.WiiU;

public sealed class RepackService()
{
    public static async Task<IReadOnlyList<TitleInputEntry>> PeekFileAsync(string path, string keysTxtPath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var sources = UnpackService.OpenAll(path, keysTxtPath);

            try
            {
                var rows = new List<TitleInputEntry>();

                for (int i = 0; i < sources.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    rows.Add(BuildRow(path, isFolder: false, subTitleIndex: i, sources[i]));
                }

                return (IReadOnlyList<TitleInputEntry>)rows;
            }
            finally
            {
                foreach (var s in sources) s.Dispose();
            }
        }, ct);
    }

    public static TitleInputEntry PeekFolder(string folderPath)
    {
        using var source = new FolderTitleSource(folderPath);

        return BuildRow(folderPath, isFolder: true, subTitleIndex: 0, source);
    }

    public static IReadOnlyList<TitleInputEntry> ScanUnpacked(string outputPath, Action<string, LogLevel>? log = null)
    {
        string unpackedRoot = Path.Combine(outputPath, "unpacked");

        if (!Directory.Exists(unpackedRoot)) 
            return [];

        var rows = new List<TitleInputEntry>();

        foreach (string dir in Directory.EnumerateDirectories(unpackedRoot))
        {
            try { rows.Add(PeekFolder(dir)); }
            catch (Exception ex) { log($"'{dir}' 폴더를 읽지 못했습니다: {ex.Message}", LogLevel.Error); }
        }

        return rows;
    }

    private static TitleInputEntry BuildRow(string path, bool isFolder, int subTitleIndex, ITitleSource source)
    {
        int fileCount = source.EnumerateFiles().Count();

        return new TitleInputEntry
        {
            Path = path,
            IsFolder = isFolder,
            SubTitleIndex = subTitleIndex,
            TitleIdHex = source.TitleIdHex,
            TitleVersion = source.TitleVersion,
            Kind = TitleInputEntry.GuessKind(source.TitleIdHex),
            FileCount = fileCount,
        };
    }

    private static ITitleSource ReopenSource(TitleInputEntry entry, string keysTxtPath)
    {
        if (entry.IsFolder)
            return new FolderTitleSource(entry.Path);

        var sources = UnpackService.OpenAll(entry.Path, keysTxtPath);

        for (int i = 0; i < sources.Count; i++)
            if (i != entry.SubTitleIndex) sources[i].Dispose();

        return sources[entry.SubTitleIndex];
    }

    public static async Task UnpackAsync(IReadOnlyList<TitleInputEntry> entries, string keysTxtPath, string outputPath, Action<ProgressInfo>? progress = null, Action<string, LogLevel>? log = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            string unpackedRoot = Path.Combine(outputPath, "unpacked");
            var sw = Stopwatch.StartNew();

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                using var source = ReopenSource(entry, keysTxtPath);

                log($"[{entry.Kind}] {source.TitleIdHex}_v{source.TitleVersion} 언팩 중...", LogLevel.Info);

                string destFolder = Path.Combine(unpackedRoot, $"{source.TitleIdHex}_v{source.TitleVersion}");

                Directory.CreateDirectory(destFolder);

                source.ExtractTo(destFolder,
                    onFileProgress: (done, total, filePath) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Invoke(new ProgressInfo
                        {
                            Percent = total > 0 ? (int)(done * 100.0 / total) : 100,
                            Label = $"[{entry.Kind}] {filePath}",
                            TimeInfo = $"{sw.Elapsed:mm\\:ss} 경과",
                            Speed = string.Empty,
                        });
                    },
                    cancellationToken: ct);
            }
        }, ct);
    }

    public static async Task RepackAsync(IReadOnlyList<TitleInputEntry> entries, string keysTxtPath, string outputPath, Action<ProgressInfo>? progress = null, Action<string, LogLevel>? log = null, CancellationToken ct = default)
    {
        if (entries.Count == 0)
            throw new InvalidOperationException("패킹할 타이틀이 없습니다.");

        await Task.Run(() =>
        {
            Directory.CreateDirectory(outputPath);
            var sources = entries.Select(e => ReopenSource(e, keysTxtPath)).ToList();
            try
            {
                var repackEntries = new List<RepackEntry>();
                for (int i = 0; i < entries.Count; i++)
                    repackEntries.Add(new RepackEntry(sources[i], entries[i].PatchPath));

                string baseTitleIdHex = sources[0].TitleIdHex;
                string outputWuaPath = Utils.GetUniqueFilePath(Path.Combine(outputPath, $"{baseTitleIdHex}_Repack.wua"));

                var sw = Stopwatch.StartNew();
                WiiURepackService.RepackMultiple(
                    repackEntries,
                    outputWuaPath,
                    onFileProgress: (done, total, path) =>
                    {
                        progress?.Invoke(new ProgressInfo
                        {
                            Percent = total > 0 ? (int)(done * 100.0 / total) : 100,
                            Label = path,
                            TimeInfo = $"{sw.Elapsed:mm\\:ss} 경과",
                            Speed = string.Empty,
                        });
                    },
                    ct: ct);

                log?.Invoke($"완료: {outputWuaPath}", LogLevel.Ok);
            }
            finally
            {
                foreach (var s in sources) 
                    s.Dispose();
            }
        }, ct);
    }
}