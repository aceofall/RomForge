using Patch.Core;
using System.IO.Compression;
using Vita.Core.Models;

namespace Vita.Core.Services;

public static class VitaPatchOnlyBuilder
{
    private static readonly HashSet<string> PatchExtensions = new(StringComparer.OrdinalIgnoreCase) { ".xdelta", ".xdelta3", ".ips", ".ups", ".bps", ".ppf", ".aps" };

    public static async Task<VitaPatchOnlyResult> BuildAsync(string sourcePath, string patchPath, string outputZipPath, VitaOutputTarget target, Action<string> log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var entry = new VitaBatchSourceEntry { Kind = VitaSourceKind.ZipOrFolder, Path = sourcePath };

        return await BuildFromEntriesAsync([entry], patchPath, outputZipPath, target, log, progress, ct);
    }

    public static async Task<VitaPatchOnlyResult> BuildFromPkgAsync(string pkgPath, string license, string patchPath, string outputZipPath, VitaOutputTarget target, Action<string> log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var probe = VitaPkgProbe.Probe(pkgPath);
        var entry = new VitaBatchSourceEntry { Kind = VitaSourceKind.Pkg, Path = pkgPath, License = license, Probe = probe };

        return await BuildFromEntriesAsync([entry], patchPath, outputZipPath, target, log, progress, ct);
    }

    public static async Task<VitaPatchOnlyResult> BuildFromPkgBatchAsync(List<VitaPkgBatchEntry> entries, string patchPath, string outputZipPath, VitaOutputTarget target, Action<string> log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var mapped = entries.Select(e => new VitaBatchSourceEntry
        {
            Kind = VitaSourceKind.Pkg,
            Path = e.PkgPath,
            License = e.License,
            PatchPath = e.PatchPath,
            Probe = e.Probe
        }).ToList();

        return await BuildFromEntriesAsync(mapped, patchPath, outputZipPath, target, log, progress, ct);
    }

    public static async Task<VitaPatchOnlyResult> BuildFromEntriesAsync(List<VitaBatchSourceEntry> entries, string defaultPatchPath, string outputZipPath, VitaOutputTarget target, Action<string> log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var ownedAccessors = new List<IVitaSourceAccessor>();

        try
        {
            var items = new List<VitaSourceItem>();

            foreach (var entry in entries)
            {
                if (entry.Kind == VitaSourceKind.Pkg)
                {
                    if (entry.Probe is null)
                        throw new InvalidOperationException($"PKG 항목에 Probe 정보가 없습니다: {entry.Path}");

                    var accessor = new PkgSourceAccessor(entry.Path, entry.License);

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
                        }
                    }
                }
            }

            return await BuildCoreAsync(items, defaultPatchPath, outputZipPath, target, log, progress, ct);
        }
        finally
        {
            foreach (var accessor in ownedAccessors)
                accessor.Dispose();
        }
    }

    private static async Task<VitaPatchOnlyResult> BuildCoreAsync(List<VitaSourceItem> items, string defaultPatchPath, string outputZipPath, VitaOutputTarget target, Action<string> log, IProgress<double>? progress, CancellationToken ct)
    {
        var patchContexts = new Dictionary<string, PatchContext>(StringComparer.OrdinalIgnoreCase);
        var writtenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int matched = 0;
        int success = 0;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);

            using var zipStream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);
            var appByTitle = items.Where(i => i.Category == VitaContentCategory.App).ToDictionary(i => i.TitleId, StringComparer.OrdinalIgnoreCase);
            var patchByTitle = items.Where(i => i.Category == VitaContentCategory.Patch).ToDictionary(i => i.TitleId, StringComparer.OrdinalIgnoreCase);
            var addcontItems = items.Where(i => i.Category == VitaContentCategory.Addcont);
            var titleIds = appByTitle.Keys.Union(patchByTitle.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var titleId in titleIds)
            {
                ct.ThrowIfCancellationRequested();

                var patchOwnedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (patchByTitle.TryGetValue(titleId, out var patchItem))
                {
                    patchOwnedRelativePaths = TryGetOwnedPaths(patchItem, log);

                    log($"[patch] {patchItem.TitleId}: 이 소스가 소유한 파일 {patchOwnedRelativePaths.Count}개 확인됨 (app에서는 스킵)");

                    string? appWorkBinRel = null;

                    if (appByTitle.TryGetValue(titleId, out var appItemForWorkBin))
                    {
                        var appAccessor = appItemForWorkBin.Accessor!;
                        string candidateWorkBin = $"{appItemForWorkBin.SourcePath}/sce_sys/package/work.bin";

                        if (appAccessor.FileExists(candidateWorkBin))
                            appWorkBinRel = candidateWorkBin;
                    }

                    var patchCtx = GetPatchContext(patchContexts, patchItem.PatchPathOverride ?? defaultPatchPath, log);
                    var r = await ProcessItemAsync(patchItem, patchCtx, target, zip, writtenEntries, null, appWorkBinRel, log, ct, progress);

                    matched += r.matched;
                    success += r.success;
                }

                if (appByTitle.TryGetValue(titleId, out var appItem))
                {
                    var appCtx = GetPatchContext(patchContexts, appItem.PatchPathOverride ?? defaultPatchPath, log);
                    var r = await ProcessItemAsync(appItem, appCtx, target, zip, writtenEntries, patchOwnedRelativePaths, null, log, ct, progress);

                    matched += r.matched;
                    success += r.success;
                }
            }

            foreach (var addcontItem in addcontItems)
            {
                ct.ThrowIfCancellationRequested();

                var dlcCtx = GetPatchContext(patchContexts, addcontItem.PatchPathOverride ?? defaultPatchPath, log);
                var r = await ProcessItemAsync(addcontItem, dlcCtx, target, zip, writtenEntries, null, null, log, ct, progress);

                matched += r.matched;
                success += r.success;
            }

            return new VitaPatchOnlyResult { MatchedCandidates = matched, PatchedSuccessfully = success };
        }
        finally
        {
            foreach (var ctx in patchContexts.Values)
                ctx.Accessor.Dispose();
        }
    }

    private sealed class PatchContext
    {
        public required IVitaSourceAccessor Accessor { get; init; }

        public required Dictionary<string, string> PatchFiles { get; init; }

        public required List<string> RawOverwriteFiles { get; init; }
    }

    private static PatchContext GetPatchContext(Dictionary<string, PatchContext> cache, string patchPath, Action<string> log)
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

    private static Dictionary<string, string> BuildPatchFileMap(List<string> allPatchFiles, Action<string> log)
    {
        var groups = allPatchFiles
            .Where(f => PatchExtensions.Contains(Path.GetExtension(f)))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f)!, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var list = group.ToList();

            if (list.Count > 1)
                log($"패치 대상 '{group.Key}'에 대한 패치 파일이 {list.Count}개 중복됨: {string.Join(", ", list)} - 첫 번째({list[0]})만 사용함");

            map[group.Key] = list[0];
        }

        return map;
    }

    private static HashSet<string> TryGetOwnedPaths(VitaSourceItem item, Action<string> log)
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
            log($"{item.Category} {item.TitleId}: 파일 목록 확인 실패 - {ex.Message}");
        }

        return set;
    }

    private static string NormalizeZipPath(string path)
    {
        string normalized = path.Replace('\\', '/');

        while (normalized.Contains("//"))
            normalized = normalized.Replace("//", "/");

        return normalized.Trim('/');
    }

    private static async Task<(int matched, int success)> ProcessItemAsync(VitaSourceItem item, PatchContext patchCtx, VitaOutputTarget target,
        ZipArchive zip, HashSet<string> writtenEntries, HashSet<string>? skipRelativePaths, string? fallbackWorkBinRel, Action<string> log, CancellationToken ct, IProgress<double>? progress)
    {
        var source = item.Accessor ?? throw new InvalidOperationException("소스 accessor가 없습니다.");
        var patch = patchCtx.Accessor;
        var patchFiles = patchCtx.PatchFiles;
        var rawOverwriteFiles = patchCtx.RawOverwriteFiles;
        string workBinRel = $"{item.SourcePath}/sce_sys/package/work.bin";

        if (!source.FileExists(workBinRel))
        {
            if (fallbackWorkBinRel != null && source.FileExists(fallbackWorkBinRel))
                workBinRel = fallbackWorkBinRel;
            else
            {
                log($"{item.Category} {item.TitleId}: work.bin 없음, 건너뜀");
                return (0, 0);
            }
        }

        var license = WorkBinReader.Read(source, workBinRel);
        var table = VitaNoNpDrmDecryptor.ParseFileTable(source, item.SourcePath);
        int matched = 0;
        int success = 0;

        for (int i = 0; i < table.Entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var entry = table.Entries[i];

            if (entry.Type.IsDirectory())
                continue;

            string relativePath = entry.RelativePath ?? entry.Name;

            if (skipRelativePaths != null && skipRelativePaths.Contains(relativePath))
                continue;

            string baseName = Path.GetFileName(relativePath);

            if (!patchFiles.TryGetValue(baseName, out var patchFileRel))
                continue;

            matched++;

            string srcRel = $"{item.SourcePath}/{relativePath.Replace('\\', '/')}";

            if (!source.FileExists(srcRel))
            {
                log($"[{item.Category}] {relativePath}: 원본 파일 없음");
                continue;
            }

            try
            {
                string prefix = GetPrefix(item.Category, target);
                string entryPath = item.Category == VitaContentCategory.Addcont
                    ? NormalizeZipPath($"{prefix}/{item.TitleId}/{item.ContentIdSuffix}/{relativePath}")
                    : NormalizeZipPath($"{prefix}/{item.TitleId}/{relativePath}");

                if (!writtenEntries.Add(entryPath))
                {
                    log($"[{item.Category}] {relativePath}: 이미 같은 경로로 추가된 항목이라 건너뜀 (중복)");
                    continue;
                }

                byte[] sourceBytes = VitaNoNpDrmDecryptor.DecryptEntry(source, item.SourcePath, license.Klicensee, entry, table.UnicvEntries[i], table.FilesSalt, out string? warning);

                if (warning != null)
                    log($"[{item.Category}] {warning}");

                byte[] patchBytes = patch.ReadAllBytes(patchFileRel);
                byte[] patchedBytes = await UniversalPatcher.ApplyPatchAsync(sourceBytes, patchBytes, ct: ct);
                var zipEntry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);

                using (var entryStream = zipEntry.Open())
                    await entryStream.WriteAsync(patchedBytes, ct);

                success++;

                log($"[{item.Category}] {relativePath}: 패치 성공");
            }
            catch (Exception ex)
            {
                log($"[{item.Category}] {relativePath}: 패치 실패 - {ex.Message}");
            }

            progress?.Report(matched == 0 ? 0 : (double)success / matched);
        }

        if (item.Category != VitaContentCategory.Addcont)
        {
            foreach (var rawRel in rawOverwriteFiles)
            {
                ct.ThrowIfCancellationRequested();

                string normalizedRawRel = rawRel.Replace('\\', '/');

                if (skipRelativePaths != null && skipRelativePaths.Contains(normalizedRawRel))
                    continue;

                string srcRel = $"{item.SourcePath}/{normalizedRawRel}";

                if (!source.FileExists(srcRel))
                    continue;

                matched++;

                try
                {
                    string prefix = GetPrefix(item.Category, target);
                    string entryPath = item.Category == VitaContentCategory.Addcont
                        ? NormalizeZipPath($"{prefix}/{item.TitleId}/{item.ContentIdSuffix}/{normalizedRawRel}")
                        : NormalizeZipPath($"{prefix}/{item.TitleId}/{normalizedRawRel}");

                    if (!writtenEntries.Add(entryPath))
                    {
                        log($"[{item.Category}] {normalizedRawRel}: 이미 같은 경로로 추가된 항목이라 건너뜀 (중복)");
                        continue;
                    }

                    byte[] rawBytes = patch.ReadAllBytes(rawRel);
                    var zipEntry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);

                    using (var entryStream = zipEntry.Open())
                        await entryStream.WriteAsync(rawBytes, ct);

                    success++;

                    log($"[{item.Category}] {normalizedRawRel}: 원본 대체 파일로 그대로 복사됨");
                }
                catch (Exception ex)
                {
                    log($"[{item.Category}] {normalizedRawRel}: 복사 실패 - {ex.Message}");
                }

                progress?.Report(matched == 0 ? 0 : (double)success / matched);
            }
        }

        if (target == VitaOutputTarget.Emu && WorkBinReader.TryGetTitleIdFromContentId(license.ContentId, out string licenseTitleId))
        {
            string licenseEntryPath = NormalizeZipPath($"license/{licenseTitleId}/{license.ContentId}.rif");

            if (writtenEntries.Add(licenseEntryPath))
            {
                var licenseEntry = zip.CreateEntry(licenseEntryPath, CompressionLevel.Optimal);
                byte[] workBinBytes = source.ReadAllBytes(workBinRel);
                using var es = licenseEntry.Open();

                await es.WriteAsync(workBinBytes, ct);
            }
        }

        return (matched, success);
    }

    private static string GetPrefix(VitaContentCategory category, VitaOutputTarget target) => (category, target) switch
    {
        (VitaContentCategory.App, VitaOutputTarget.Emu) => "app",
        (VitaContentCategory.Patch, VitaOutputTarget.Emu) => "app",
        (VitaContentCategory.Addcont, VitaOutputTarget.Emu) => "addcont",
        (VitaContentCategory.App, VitaOutputTarget.Retail) => "rePatch",
        (VitaContentCategory.Patch, VitaOutputTarget.Retail) => "rePatch",
        (VitaContentCategory.Addcont, VitaOutputTarget.Retail) => "reAddcont",
        _ => throw new NotSupportedException()
    };
}