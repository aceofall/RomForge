using Common;
using NSW.Utils;
using RomForge.Core.Models.WiiU;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using WiiU.Core.Models;
using WiiU.Core.Services;

namespace RomForge.Core.Services.WiiU;

public sealed class RepackService()
{
    public static async Task<IReadOnlyList<TitleInputEntry>> PeekFileAsync(string path, string keysTxtPath, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            var sources = UnpackService.OpenAll(path, keysTxtPath);

            try
            {
                var rows = new List<TitleInputEntry>();

                for (int i = 0; i < sources.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    rows.Add(await BuildRowAsync(path, keysTxtPath, isFolder: false, subTitleIndex: i, sources[i]));
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
        using ITitleSource source = WupTitleSource.LooksLikeWupFolder(folderPath)
            ? new WupTitleSource(folderPath)
            : new FolderTitleSource(folderPath);

        return BuildRowFromFolder(folderPath, source);
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

    private static async Task<TitleInputEntry> BuildRowAsync(string path, string keysTxtPath, bool isFolder, int subTitleIndex, ITitleSource source)
    {
        int fileCount = source.EnumerateFiles().Count();

        string? titleName = null;
        ImageSource? icon = null;

        try
        {
            var meta = await WiiUMetadataExtractor.Extract(path, keysTxtPath);

            if (meta is not null)
            {
                titleName = meta.Title;

                if (meta.Image is { Length: > 0 } pngBytes)
                    icon = TryLoadIcon(pngBytes);
            }
        }
        catch
        {
        }

        return new TitleInputEntry(path, source.TitleIdHex)
        {
            IsFolder = isFolder,
            SubTitleIndex = subTitleIndex,
            TitleVersion = source.TitleVersion,
            FileCount = fileCount,
            TitleName = titleName,
            Icon = icon,
        };
    }

    private static TitleInputEntry BuildRowFromFolder(string folderPath, ITitleSource source)
    {
        int fileCount = source.EnumerateFiles().Count();

        string? titleName = null;
        ImageSource? icon = null;

        try
        {
            var meta = source is WupTitleSource
                ? WiiUMetadataExtractor.ExtractFromTitleSource(source)
                : WiiUMetadataExtractor.ExtractFromFolder(folderPath);

            if (meta is not null)
            {
                titleName = meta.Title;

                if (meta.Image is { Length: > 0 } pngBytes)
                    icon = TryLoadIcon(pngBytes);
            }
        }
        catch
        {
        }

        return new TitleInputEntry(folderPath, source.TitleIdHex)
        {
            IsFolder = true,
            SubTitleIndex = 0,
            TitleVersion = source.TitleVersion,
            FileCount = fileCount,
            TitleName = titleName,
            Icon = icon,
        };
    }

    private static BitmapImage? TryLoadIcon(byte[] pngBytes)
    {
        try
        {
            using var ms = new MemoryStream(pngBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ITitleSource ReopenSource(TitleInputEntry entry, string keysTxtPath)
    {
        if (entry.IsFolder)
        {
            return WupTitleSource.LooksLikeWupFolder(entry.FilePath)
                ? new WupTitleSource(entry.FilePath)
                : new FolderTitleSource(entry.FilePath);
        }

        var sources = UnpackService.OpenAll(entry.FilePath, keysTxtPath);

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

                // 폴더명은 반드시 Role로 보정된 title ID를 써야 한다. 원본 그대로(source.TitleIdHex)를 쓰면
                // 본편/업데이트/DLC가 실제로는 같은 카테고리로 찍혀있는 경우(흔함) 서로 다른 타이틀인데도
                // 같은 폴더 이름으로 언팩돼서 파일이 섞이거나 리팩 시 매칭이 꼬인다.
                string correctedTitleIdHex = entry.RoleCorrectedTitleIdHex;

                log($"[{entry.Kind}] {correctedTitleIdHex}_v{source.TitleVersion} 언팩 중...", LogLevel.Info);

                string destFolder = Path.Combine(unpackedRoot, $"{correctedTitleIdHex}_v{source.TitleVersion}");

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

                // meta.xml이 있으면 title_id를 보정된 값으로 다시 써준다 — FolderTitleSource가 폴더명보다
                // meta.xml의 title_id를 우선해서 읽기 때문에, 이걸 안 고치면 나중에 이 폴더를 다시 스캔했을 때
                // (리빌드 시 ScanUnpacked) 또 본편 카테고리로 잘못 읽혀서 Role 자동분류가 깨진다.
                PatchMetaXmlTitleId(destFolder, correctedTitleIdHex);
            }
        }, ct);
    }

    /// <summary>언팩된 폴더의 meta/meta.xml 안 title_id를 보정된 값으로 다시 쓴다. meta.xml이 없거나
    /// title_id 엘리먼트가 없으면 조용히 넘어간다(부가 보정이라 실패해도 언팩 자체는 계속 진행되어야 함).</summary>
    private static void PatchMetaXmlTitleId(string destFolder, string correctedTitleIdHex)
    {
        string metaXmlPath = Path.Combine(destFolder, "meta", "meta.xml");

        if (!File.Exists(metaXmlPath))
            return;

        try
        {
            var doc = XDocument.Load(metaXmlPath);
            var titleIdElement = doc.Root?.Element("title_id");

            if (titleIdElement is not null)
            {
                titleIdElement.Value = correctedTitleIdHex;
                doc.Save(metaXmlPath);
            }
        }
        catch
        {
            // meta.xml 보정은 부가 기능이므로 실패해도 언팩 자체는 계속 진행한다.
        }
    }

    public static async Task RepackAsync(IReadOnlyList<TitleInputEntry> entries, string keysTxtPath, string outputPath, RepackOutputFormat format, Action<ProgressInfo>? progress = null, Action<string, LogLevel>? log = null, CancellationToken ct = default)
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
                    repackEntries.Add(new RepackEntry(sources[i], entries[i].PatchPath, TitleIdHexOverride: entries[i].RoleCorrectedTitleIdHex));

                string fileName = entries[0].DisplayName;
                fileName = NspNameBuilder.SafeFileName(fileName);

                if (format == RepackOutputFormat.Wup)
                {
                    RepackToWup(repackEntries, sources, entries, fileName, outputPath, progress, log, ct);
                    return;
                }

                string outputWuaPath = Utils.GetUniqueFilePath(Path.Combine(outputPath, $"{fileName}_Repack.wua"));

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

    /// <summary>
    /// WUA 리팩과 달리, WUP는 소스(베이스/업데이트/DLC 등) 개수만큼 별도의 WUP 폴더를 각각 만든다
    /// (실제 NUS 배포 방식대로 타이틀당 tmd/tik 세트 하나). 소스 하나마다:
    /// code/는 파일별로 개별 raw 콘텐츠, meta/는 파일별로 개별 hashed 콘텐츠, 그 외(content/ 등)는
    /// 하나의 hashed 콘텐츠로 묶는다 — NUSPacker의 기본 규칙과 동일한 방식.
    /// title ID는 entry.Role 기준으로 보정한 값(RoleCorrectedTitleIdHex)을 쓴다 — 업데이트/DLC로
    /// 지정한 폴더의 실제 title ID가 본편 카테고리로 찍혀있는 흔한 경우에도 결과물이 올바르게 나오게 하기 위함.
    /// </summary>
    private static void RepackToWup(List<RepackEntry> repackEntries, List<ITitleSource> sources, IReadOnlyList<TitleInputEntry> entries, string fileName, string outputPath, Action<ProgressInfo>? progress, Action<string, LogLevel>? log, CancellationToken ct)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var source = sources[i];
            string? patchPath = repackEntries[i].PatchFolder;

            var codeFiles = new List<WupFileEntry>();
            var metaFiles = new List<WupFileEntry>();
            var contentFiles = new List<WupFileEntry>();

            foreach (string relPath in source.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();

                byte[] data;

                string? patchFilePath = patchPath is null ? null : Path.Combine(patchPath, relPath.Replace('/', Path.DirectorySeparatorChar));

                if (patchFilePath is not null && File.Exists(patchFilePath))
                {
                    data = File.ReadAllBytes(patchFilePath);
                }
                else
                {
                    using var stream = source.OpenRead(relPath);
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    data = ms.ToArray();
                }

                var entry = new WupFileEntry(relPath, data);

                if (relPath.StartsWith("code/", StringComparison.OrdinalIgnoreCase))
                    codeFiles.Add(entry);
                else if (relPath.StartsWith("meta/", StringComparison.OrdinalIgnoreCase))
                    metaFiles.Add(entry);
                else
                    contentFiles.Add(entry);
            }

            var groups = new List<WupContentGroup>();

            foreach (var f in codeFiles)
                groups.Add(new WupContentGroup { Hashed = false, Files = [f] });

            foreach (var f in metaFiles)
                groups.Add(new WupContentGroup { Hashed = true, Files = [f] });

            if (contentFiles.Count > 0)
                groups.Add(new WupContentGroup { Hashed = true, Files = contentFiles });

            ulong titleId = entries[i].GetRoleCorrectedTitleId();
            ushort titleVersion = (ushort)source.TitleVersion;

            string suffix = sources.Count > 1 ? $"_{i}_{entries[i].Kind}_v{titleVersion}" : "";
            string wupFolder = Utils.GetUniqueFilePath(Path.Combine(outputPath, $"{fileName}{suffix}_WUP"));

            log?.Invoke($"WUP로 패키징 중 ({i + 1}/{sources.Count}): {wupFolder}", LogLevel.Info);

            WupPacker.Pack(wupFolder, titleId, titleVersion, groups);

            progress?.Invoke(new ProgressInfo
            {
                Percent = (int)((i + 1) * 100.0 / sources.Count),
                Label = $"WUP 생성 완료: {titleId:x16}_v{titleVersion}",
                TimeInfo = string.Empty,
                Speed = string.Empty,
            });

            log?.Invoke($"완료: {wupFolder}", LogLevel.Ok);
        }
    }
}