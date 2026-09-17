using Common;
using Patch.Core;
using System.IO.Compression;
using Vita.Core.Models;

namespace Vita.Core.Services;

public static class VitaPatchOutputBuilder
{
    public static async Task<VitaMergeResult> BuildMergedFromEntriesAsync(List<VitaBatchSourceEntry> entries, string defaultPatchPath, string outputZipPath, VitaOutputTarget target, Action<string, LogLevel> log, IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var (items, ownedAccessors) = VitaPatchShared.LoadItems(entries, log);

        try
        {
            return await BuildCoreAsync(items, defaultPatchPath, outputZipPath, target, log, progress, ct);
        }
        finally
        {
            foreach (var accessor in ownedAccessors)
                accessor.Dispose();
        }
    }

    private static async Task<VitaMergeResult> BuildCoreAsync(List<VitaSourceItem> items, string defaultPatchPath, string outputZipPath, VitaOutputTarget target, Action<string, LogLevel> log, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var patchContexts = new Dictionary<string, VitaPatchShared.PatchContext>(StringComparer.OrdinalIgnoreCase);
        var writtenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appByTitle = items.Where(i => i.Category == VitaContentCategory.App).ToDictionary(i => i.TitleId, StringComparer.OrdinalIgnoreCase);
        var patchByTitle = items.Where(i => i.Category == VitaContentCategory.Patch).ToDictionary(i => i.TitleId, StringComparer.OrdinalIgnoreCase);
        var addcontItems = items.Where(i => i.Category == VitaContentCategory.Addcont).ToList();
        var titleIds = appByTitle.Keys.Union(patchByTitle.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        long totalBytes = EstimateTotalBytes(titleIds, appByTitle, patchByTitle, addcontItems, defaultPatchPath, patchContexts, log);
        var reporter = new ProgressReporter("게임+패치 병합 중", string.Empty, totalBytes, progress);
        int totalFiles = 0;
        int patchCandidates = 0;
        int patchedSuccess = 0;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);

            using var zipStream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

            foreach (var titleId in titleIds)
            {
                ct.ThrowIfCancellationRequested();

                var patchOwnedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (patchByTitle.TryGetValue(titleId, out var patchItem))
                {
                    patchOwnedRelativePaths = VitaPatchShared.TryGetOwnedPaths(patchItem, log);

                    string? appWorkBinRel = null;

                    if (appByTitle.TryGetValue(titleId, out var appItemForWorkBin))
                    {
                        var appAccessor = appItemForWorkBin.Accessor!;
                        string candidateWorkBin = $"{appItemForWorkBin.SourcePath}/sce_sys/package/work.bin";

                        if (appAccessor.FileExists(candidateWorkBin))
                            appWorkBinRel = candidateWorkBin;
                    }

                    var patchCtx = VitaPatchShared.GetPatchContext(patchContexts, patchItem.PatchPathOverride ?? defaultPatchPath, log);
                    var r = await ProcessItemAsync(patchItem, patchCtx, target, zip, writtenEntries, null, appWorkBinRel, reporter, log, ct);

                    totalFiles += r.total;
                    patchCandidates += r.matched;
                    patchedSuccess += r.patchedSuccess;

                    LogItemResult(log, patchItem, r);
                }

                if (appByTitle.TryGetValue(titleId, out var appItem))
                {
                    var appCtx = VitaPatchShared.GetPatchContext(patchContexts, appItem.PatchPathOverride ?? defaultPatchPath, log);
                    var r = await ProcessItemAsync(appItem, appCtx, target, zip, writtenEntries, patchOwnedRelativePaths, null, reporter, log, ct);

                    totalFiles += r.total;
                    patchCandidates += r.matched;
                    patchedSuccess += r.patchedSuccess;

                    LogItemResult(log, appItem, r);
                }
            }

            foreach (var addcontItem in addcontItems)
            {
                ct.ThrowIfCancellationRequested();

                var dlcCtx = VitaPatchShared.GetPatchContext(patchContexts, addcontItem.PatchPathOverride ?? defaultPatchPath, log);
                var r = await ProcessItemAsync(addcontItem, dlcCtx, target, zip, writtenEntries, null, null, reporter, log, ct);

                totalFiles += r.total;
                patchCandidates += r.matched;
                patchedSuccess += r.patchedSuccess;

                LogItemResult(log, addcontItem, r);
            }

            reporter.ForceReport();

            return new VitaMergeResult { TotalFiles = totalFiles, PatchCandidates = patchCandidates, PatchedSuccessfully = patchedSuccess };
        }
        finally
        {
            foreach (var ctx in patchContexts.Values)
                ctx.Accessor.Dispose();
        }
    }

    private static long EstimateTotalBytes(List<string> titleIds, Dictionary<string, VitaSourceItem> appByTitle, Dictionary<string, VitaSourceItem> patchByTitle,
        List<VitaSourceItem> addcontItems, string defaultPatchPath, Dictionary<string, VitaPatchShared.PatchContext> patchContexts, Action<string, LogLevel> log)
    {
        long total = 0;

        foreach (var titleId in titleIds)
        {
            var patchOwnedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (patchByTitle.TryGetValue(titleId, out var patchItem))
            {
                patchOwnedRelativePaths = VitaPatchShared.TryGetOwnedPaths(patchItem, log);

                string? appWorkBinRel = null;

                if (appByTitle.TryGetValue(titleId, out var appItemForWorkBin) && appItemForWorkBin.Accessor!.FileExists($"{appItemForWorkBin.SourcePath}/sce_sys/package/work.bin"))
                    appWorkBinRel = $"{appItemForWorkBin.SourcePath}/sce_sys/package/work.bin";

                var patchCtx = VitaPatchShared.GetPatchContext(patchContexts, patchItem.PatchPathOverride ?? defaultPatchPath, log);

                total += EstimateItemBytes(patchItem, patchCtx, null, appWorkBinRel);
            }

            if (appByTitle.TryGetValue(titleId, out var appItem))
            {
                var appCtx = VitaPatchShared.GetPatchContext(patchContexts, appItem.PatchPathOverride ?? defaultPatchPath, log);

                total += EstimateItemBytes(appItem, appCtx, patchOwnedRelativePaths, null);
            }
        }

        foreach (var addcontItem in addcontItems)
        {
            var dlcCtx = VitaPatchShared.GetPatchContext(patchContexts, addcontItem.PatchPathOverride ?? defaultPatchPath, log);

            total += EstimateItemBytes(addcontItem, dlcCtx, null, null);
        }

        return total;
    }

    private static long EstimateItemBytes(VitaSourceItem item, VitaPatchShared.PatchContext patchCtx, HashSet<string>? skipRelativePaths, string? fallbackWorkBinRel)
    {
        var source = item.Accessor ?? throw new InvalidOperationException("소스 accessor가 없습니다.");

        if (VitaPatchShared.ResolveWorkBinRel(source, item, fallbackWorkBinRel) is null)
            return 0;

        long total = 0;

        try
        {
            var table = VitaNoNpDrmDecryptor.ParseFileTable(source, item.SourcePath);

            foreach (var entry in table.Entries)
            {
                if (entry.Type.IsDirectory())
                    continue;

                string relativePath = entry.RelativePath ?? entry.Name;

                if (skipRelativePaths != null && skipRelativePaths.Contains(relativePath))
                    continue;

                string srcRel = $"{item.SourcePath}/{relativePath.Replace('\\', '/')}";

                if (source.FileExists(srcRel))
                    total += entry.Size;
            }
        }
        catch
        {
            return total;
        }

        if (item.Category != VitaContentCategory.Addcont)
        {
            foreach (var rawRel in patchCtx.RawOverwriteFiles)
            {
                string normalizedRawRel = rawRel.Replace('\\', '/');

                if (skipRelativePaths != null && skipRelativePaths.Contains(normalizedRawRel))
                    continue;

                string srcRel = $"{item.SourcePath}/{normalizedRawRel}";

                if (source.FileExists(srcRel))
                    total += patchCtx.Accessor.GetFileSize(rawRel);
            }
        }

        return total;
    }

    private static void LogItemResult(Action<string, LogLevel> log, VitaSourceItem item, (int total, int matched, int patchedSuccess) r)
    {
        if (r.total == 0)
            return;

        string name = item.Category == VitaContentCategory.Addcont ? $"{item.TitleId}/{item.ContentIdSuffix}" : item.TitleId;

        if (r.matched == 0)
            log($"[{item.Category}] {name}: {r.total}개 파일 병합 완료 (패치 없음)", LogLevel.Ok);
        else if (r.patchedSuccess == r.matched)
            log($"[{item.Category}] {name}: {r.total}개 파일 병합 완료 (패치 {r.matched}개 적용)", LogLevel.Ok);
        else
            log($"[{item.Category}] {name}: {r.total}개 파일 병합 완료 (패치 {r.patchedSuccess}/{r.matched}개만 적용, {r.matched - r.patchedSuccess}개는 원본 유지)", LogLevel.Error);
    }

    private static async Task<(int total, int matched, int patchedSuccess)> ProcessItemAsync(VitaSourceItem item, VitaPatchShared.PatchContext patchCtx, VitaOutputTarget target,
        ZipArchive zip, HashSet<string> writtenEntries, HashSet<string>? skipRelativePaths, string? fallbackWorkBinRel, ProgressReporter reporter, Action<string, LogLevel> log, CancellationToken ct)
    {
        var source = item.Accessor ?? throw new InvalidOperationException("소스 accessor가 없습니다.");
        var patch = patchCtx.Accessor;
        var patchFiles = patchCtx.PatchFiles;
        var rawOverwriteFiles = patchCtx.RawOverwriteFiles;
        string? workBinRel = VitaPatchShared.ResolveWorkBinRel(source, item, fallbackWorkBinRel);

        if (workBinRel is null)
        {
            log($"{item.Category} {item.TitleId}: work.bin 없음, 건너뜀", LogLevel.Error);
            return (0, 0, 0);
        }

        var license = WorkBinReader.Read(source, workBinRel);
        var table = VitaNoNpDrmDecryptor.ParseFileTable(source, item.SourcePath);
        int total = 0;
        int matched = 0;
        int patchedSuccess = 0;

        for (int i = 0; i < table.Entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var entry = table.Entries[i];

            if (entry.Type.IsDirectory())
                continue;

            string relativePath = entry.RelativePath ?? entry.Name;

            if (skipRelativePaths != null && skipRelativePaths.Contains(relativePath))
                continue;

            string srcRel = $"{item.SourcePath}/{relativePath.Replace('\\', '/')}";

            if (!source.FileExists(srcRel))
            {
                log($"[{item.Category}] {relativePath}: 원본 파일 없음", LogLevel.Error);
                continue;
            }

            bool hasXdeltaCandidate = patchFiles.TryGetValue(Path.GetFileName(relativePath), out var patchFileRel);

            if (hasXdeltaCandidate)
                matched++;

            byte[]? outputBytes;
            bool patchApplied = false;

            try
            {
                byte[] sourceBytes = VitaNoNpDrmDecryptor.DecryptEntry(source, item.SourcePath, license.Klicensee, entry, table.UnicvEntries[i], table.FilesSalt, out string? warning);

                if (warning != null)
                    log($"[{item.Category}] {warning}", LogLevel.Highlight);

                outputBytes = sourceBytes;

                if (hasXdeltaCandidate)
                {
                    try
                    {
                        byte[] patchBytes = patch.ReadAllBytes(patchFileRel!);

                        outputBytes = await UniversalPatcher.ApplyPatchAsync(sourceBytes, patchBytes, ct: ct);
                        patchApplied = true;
                        patchedSuccess++;
                    }
                    catch (Exception ex)
                    {
                        log($"[{item.Category}] {relativePath}: 패치 실패, 원본 파일로 대체함 - {ex.Message}", LogLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                log($"[{item.Category}] {relativePath}: 복호화 실패 - {ex.Message}", LogLevel.Error);
                reporter.AddProgress(entry.Size);
                continue;
            }

            string prefix = target == VitaOutputTarget.Retail && patchApplied ? VitaPatchShared.GetPatchedPrefix(item.Category, target) : VitaPatchShared.GetBasePrefix(item.Category);
            string entryPath = item.Category == VitaContentCategory.Addcont ? VitaPatchShared.NormalizeZipPath($"{prefix}/{item.TitleId}/{item.ContentIdSuffix}/{relativePath}") : VitaPatchShared.NormalizeZipPath($"{prefix}/{item.TitleId}/{relativePath}");

            if (!writtenEntries.Add(entryPath))
            {
                log($"[{item.Category}] {relativePath}: 이미 같은 경로로 추가된 항목이라 건너뜀 (중복)", LogLevel.Highlight);
                reporter.AddProgress(entry.Size);
                continue;
            }

            var zipEntry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);

            await VitaPatchShared.WriteEntryWithProgressAsync(zipEntry, outputBytes, reporter, ct);

            total++;

            reporter.AddProgress(entry.Size);
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

                long rawSize = patch.GetFileSize(rawRel);

                matched++;

                try
                {
                    string prefix = VitaPatchShared.GetPatchedPrefix(item.Category, target);
                    string entryPath = VitaPatchShared.NormalizeZipPath($"{prefix}/{item.TitleId}/{normalizedRawRel}");

                    if (!writtenEntries.Add(entryPath))
                    {
                        log($"[{item.Category}] {normalizedRawRel}: 이미 같은 경로로 추가된 항목이라 건너뜀 (중복)", LogLevel.Highlight);
                        reporter.AddProgress(rawSize);
                        continue;
                    }

                    byte[] rawBytes = patch.ReadAllBytes(rawRel);
                    var zipEntry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);

                    await VitaPatchShared.WriteEntryWithProgressAsync(zipEntry, rawBytes, reporter, ct);

                    total++;
                    patchedSuccess++;

                    reporter.AddProgress(rawSize);
                }
                catch (Exception ex)
                {
                    log($"[{item.Category}] {normalizedRawRel}: 복사 실패 - {ex.Message}", LogLevel.Error);
                    reporter.AddProgress(rawSize);
                }
            }
        }

        if (target == VitaOutputTarget.Emu && WorkBinReader.TryGetTitleIdFromContentId(license.ContentId, out string licenseTitleId))
        {
            string licenseEntryPath = VitaPatchShared.NormalizeZipPath($"license/{licenseTitleId}/{license.ContentId}.rif");

            if (writtenEntries.Add(licenseEntryPath))
            {
                var licenseEntry = zip.CreateEntry(licenseEntryPath, CompressionLevel.Optimal);
                byte[] workBinBytes = source.ReadAllBytes(workBinRel);
                using var es = licenseEntry.Open();

                await es.WriteAsync(workBinBytes, ct);
            }
        }

        return (total, matched, patchedSuccess);
    }
}