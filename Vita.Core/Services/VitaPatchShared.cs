using Common;
using System.IO.Compression;
using Vita.Core.Models;

namespace Vita.Core.Services;

internal static class VitaPatchShared
{
    private const int ProgressChunkSize = 4 * 1024 * 1024;
    public static readonly HashSet<string> PatchExtensions = new(StringComparer.OrdinalIgnoreCase) { ".xdelta", ".xdelta3", ".ips", ".ups", ".bps", ".ppf", ".aps" };

    public sealed class PatchContext
    {
        public required IVitaSourceAccessor Accessor { get; init; }

        public required Dictionary<string, string> PatchFiles { get; init; }

        public required List<string> RawOverwriteFiles { get; init; }
    }

    public static (List<VitaSourceItem> Items, List<IVitaSourceAccessor> OwnedAccessors) LoadItems(List<VitaBatchSourceEntry> entries, Action<string, LogLevel> log)
    {
        var ownedAccessors = new List<IVitaSourceAccessor>();
        var items = new List<VitaSourceItem>();
        var appSourceByTitle = new Dictionary<string, (IVitaSourceAccessor Accessor, string SourcePath)>(StringComparer.OrdinalIgnoreCase);
        var orderedEntries = entries.OrderBy(GetEntrySortRank).ToList();

        foreach (var entry in orderedEntries)
        {
            if (entry.Kind == VitaSourceKind.Pkg)
            {
                if (entry.Probe is null)
                    throw new InvalidOperationException($"PKG 항목에 Probe 정보가 없습니다: {entry.Path}");

                IVitaSourceAccessor accessor;

                if (entry.Probe.Category == VitaContentCategory.Patch && string.IsNullOrWhiteSpace(entry.License) && appSourceByTitle.TryGetValue(entry.Probe.TitleId, out var appSource))
                {
                    string appWorkBinRel = $"{appSource.SourcePath}/sce_sys/package/work.bin";
                    var appLicense = WorkBinReader.Read(appSource.Accessor, appWorkBinRel);

                    accessor = new PkgSourceAccessor(entry.Path, appLicense.Klicensee);
                }
                else
                    accessor = new PkgSourceAccessor(entry.Path, entry.License);

                ownedAccessors.Add(accessor);

                items.Add(new VitaSourceItem
                {
                    Category = entry.Probe.Category,
                    TitleId = entry.Probe.TitleId,
                    ContentIdSuffix = entry.Probe.ContentIdSuffix,
                    SourcePath = string.Empty,
                    Accessor = accessor,
                    PatchPathOverride = entry.PatchPath
                });

                if (entry.Probe.Category == VitaContentCategory.App)
                    appSourceByTitle[entry.Probe.TitleId] = (accessor, string.Empty);
            }
            else
            {
                var accessor = VitaSourceAccessorFactory.Open(entry.Path);

                ownedAccessors.Add(accessor);

                if (entry.ItemSourcePath != null)
                {
                    items.Add(new VitaSourceItem
                    {
                        Category = entry.ItemCategory ?? throw new InvalidOperationException($"항목 카테고리가 없습니다: {entry.Path}"),
                        TitleId = entry.ItemTitleId ?? throw new InvalidOperationException($"항목 TitleId가 없습니다: {entry.Path}"),
                        ContentIdSuffix = entry.ItemContentIdSuffix,
                        SourcePath = entry.ItemSourcePath,
                        Accessor = accessor,
                        PatchPathOverride = entry.PatchPath
                    });

                    if (entry.ItemCategory == VitaContentCategory.App)
                        appSourceByTitle[entry.ItemTitleId!] = (accessor, entry.ItemSourcePath);
                }
                else
                {
                    foreach (var discovered in VitaSourcePreparer.DiscoverItems(accessor))
                    {
                        items.Add(new VitaSourceItem
                        {
                            Category = discovered.Category,
                            TitleId = discovered.TitleId,
                            ContentIdSuffix = discovered.ContentIdSuffix,
                            SourcePath = discovered.SourcePath,
                            Accessor = accessor,
                            PatchPathOverride = entry.PatchPath
                        });

                        if (discovered.Category == VitaContentCategory.App)
                            appSourceByTitle[discovered.TitleId] = (accessor, discovered.SourcePath);
                    }
                }
            }
        }

        return (items, ownedAccessors);
    }

    private static int GetEntrySortRank(VitaBatchSourceEntry entry)
    {
        var category = entry.Kind == VitaSourceKind.Pkg ? entry.Probe?.Category : entry.ItemCategory;

        return category switch
        {
            VitaContentCategory.App => 0,
            VitaContentCategory.Patch => 1,
            VitaContentCategory.Addcont => 2,
            _ => 3
        };
    }

    public static PatchContext GetPatchContext(Dictionary<string, PatchContext> cache, string patchPath, Action<string, LogLevel> log)
    {
        if (cache.TryGetValue(patchPath, out var existing))
            return existing;

        var accessor = VitaSourceAccessorFactory.Open(patchPath);
        var allPatchFiles = accessor.EnumerateAllFiles().ToList();
        var patchFiles = BuildPatchFileMap(allPatchFiles, log);
        var rawOverwriteFiles = allPatchFiles
            .Where(f => !PatchExtensions.Contains(Path.GetExtension(f)))
            .ToList();
        var ctx = new PatchContext { Accessor = accessor, PatchFiles = patchFiles, RawOverwriteFiles = rawOverwriteFiles };

        cache[patchPath] = ctx;

        return ctx;
    }

    private static Dictionary<string, string> BuildPatchFileMap(List<string> allPatchFiles, Action<string, LogLevel> log)
    {
        var groups = allPatchFiles
            .Where(f => PatchExtensions.Contains(Path.GetExtension(f)))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f)!, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var list = group.ToList();

            map[group.Key] = list[0];
        }

        return map;
    }

    public static HashSet<string> TryGetOwnedPaths(VitaSourceItem item, Action<string, LogLevel> log)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accessor = item.Accessor ?? throw new InvalidOperationException("소스 accessor가 없습니다.");

        try
        {
            var table = VitaNoNpDrmDecryptor.ParseFileTable(accessor, item.SourcePath);

            foreach (var entry in table.Entries)
            {
                if (!entry.Type.IsDirectory())
                    set.Add(entry.RelativePath ?? entry.Name);
            }
        }
        catch (Exception ex)
        {
            log($"{item.Category} {item.TitleId}: 파일 목록 확인 실패 - {ex.Message}", LogLevel.Error);
        }

        return set;
    }

    public static string? ResolveWorkBinRel(IVitaSourceAccessor source, VitaSourceItem item, string? fallbackWorkBinRel)
    {
        string workBinRel = $"{item.SourcePath}/sce_sys/package/work.bin";

        if (source.FileExists(workBinRel))
            return workBinRel;

        if (fallbackWorkBinRel != null && source.FileExists(fallbackWorkBinRel))
            return fallbackWorkBinRel;

        return null;
    }

    public static string NormalizeZipPath(string path)
    {
        string normalized = path.Replace('\\', '/');

        while (normalized.Contains("//"))
            normalized = normalized.Replace("//", "/");

        return normalized.Trim('/');
    }

    public static async Task WriteEntryWithProgressAsync(ZipArchiveEntry zipEntry, byte[] data, ProgressReporter reporter, CancellationToken ct)
    {
        using var entryStream = zipEntry.Open();

        if (data.Length == 0)
        {
            reporter.AddProgress(0);
            return;
        }

        int offset = 0;

        while (offset < data.Length)
        {
            int size = Math.Min(ProgressChunkSize, data.Length - offset);

            await entryStream.WriteAsync(data.AsMemory(offset, size), ct);
            reporter.AddProgress(size);
            offset += size;
        }
    }

    public static string GetPatchedPrefix(VitaContentCategory category, VitaOutputTarget target) => (category, target) switch
    {
        (VitaContentCategory.App, VitaOutputTarget.Emu) => "app",
        (VitaContentCategory.Patch, VitaOutputTarget.Emu) => "app",
        (VitaContentCategory.Addcont, VitaOutputTarget.Emu) => "addcont",
        (VitaContentCategory.App, VitaOutputTarget.Retail) => "rePatch",
        (VitaContentCategory.Patch, VitaOutputTarget.Retail) => "rePatch",
        (VitaContentCategory.Addcont, VitaOutputTarget.Retail) => "reAddcont",
        _ => throw new NotSupportedException()
    };

    public static string GetBasePrefix(VitaContentCategory category) => category == VitaContentCategory.Addcont ? "addcont" : "app";
}