using Common;
using NSW.Utils;
using RomForge.Core.Models.WiiU;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            try
            {
                var row = PeekFolder(dir);

                // 폴더명에 우리가 언팩할 때 남긴 Role 표시가 있으면 title ID 추측(GuessRole)보다 우선한다 —
                // 업데이트/DLC 폴더는 title ID가 본편이랑 같은 카테고리로 찍혀있는 경우가 흔해서 그것만으론
                // 정확히 구분이 안 되기 때문. 표시가 없으면(본편) GuessRole 결과를 그대로 둔다.
                string dirName = Path.GetFileName(dir);

                if (dirName.Contains("_update", StringComparison.OrdinalIgnoreCase))
                    row.Role = TitleRole.Update;
                else if (dirName.Contains("_dlc", StringComparison.OrdinalIgnoreCase))
                    row.Role = TitleRole.Dlc;

                rows.Add(row);
            }
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

                log($"[{entry.Kind}] {source.TitleIdHex}_v{source.TitleVersion} 언팩 중...", LogLevel.Info);

                // 실제 게임 데이터(meta.xml 등)는 절대 건드리지 않는다 — 업데이트/DLC 폴더의 title ID가
                // 본편이랑 같은 카테고리로 찍혀있는 경우가 흔한데, 그건 우리가 신뢰할 수 없는 소스라서
                // 고쳐써봐야 다시 신뢰할 수 있게 되는 게 아니다. 대신 우리가 완전히 통제하는 "폴더 이름"에만
                // Role 표시(_update, _dlc)를 남겨서, 나중에 이 폴더를 다시 스캔할 때(ScanUnpacked) 그 표시로
                // Role을 복원한다. 폴더명이 겹칠 걱정도 없다(본편은 접미사 없음, 업데이트/DLC는 접미사로 구분).
                string roleSuffix = entry.Role switch
                {
                    TitleRole.Update => "_update",
                    TitleRole.Dlc => "_dlc",
                    _ => "",
                };

                string destFolder = Utils.GetUniqueFilePath(Path.Combine(unpackedRoot, $"{source.TitleIdHex}_v{source.TitleVersion}{roleSuffix}"));

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

                try
                {
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
                catch
                {
                    // 취소되거나 오류가 나면 만들다 만 .wua 파일을 남겨두지 않는다.
                    if (File.Exists(outputWuaPath))
                    {
                        try { File.Delete(outputWuaPath); } catch { }
                    }

                    throw;
                }
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

            try
            {
                WupPacker.Pack(wupFolder, titleId, titleVersion, groups, ct);
            }
            catch
            {
                // 취소되거나 오류가 나면 만들다 만 이 타이틀의 WUP 폴더만 정리한다
                // (이미 완료된 이전 타이틀들의 WUP 폴더는 그대로 둔다).
                if (Directory.Exists(wupFolder))
                {
                    try { Directory.Delete(wupFolder, true); } catch { }
                }

                throw;
            }

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