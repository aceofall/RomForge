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
        var patchContexts = new Dictionary<string, PatchContext>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var appByTitle = items.Where(i => i.Category == VitaContentCategory.App).ToDictionary(i => i.TitleId, StringComparer.OrdinalIgnoreCase);
            var patchByTitle = items.Where(i => i.Category == VitaContentCategory.Patch).ToDictionary(i => i.TitleId, StringComparer.OrdinalIgnoreCase);
            var addcontItems = items.Where(i => i.Category == VitaContentCategory.Addcont).ToList();
            var titleIds = appByTitle.Keys.Union(patchByTitle.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var groups = new List<MergeGroup>();

            foreach (var titleId in titleIds)
            {
                ct.ThrowIfCancellationRequested();

                appByTitle.TryGetValue(titleId, out var appItem);
                patchByTitle.TryGetValue(titleId, out var patchItem);

                string? appWorkBinFallback = appItem != null ? $"{appItem.SourcePath}/sce_sys/package/work.bin" : null;
                var appOwner = appItem != null ? VitaPatchShared.BuildOwnerContext(appItem, null, log) : null;
                var patchOwner = patchItem != null ? VitaPatchShared.BuildOwnerContext(patchItem, appWorkBinFallback, log) : null;
                var owners = new List<OwnerContext>();

                if (appOwner != null)
                    owners.Add(appOwner);

                if (patchOwner != null)
                    owners.Add(patchOwner);

                if (owners.Count == 0)
                    continue;

                string patchPackagePath = (patchItem?.PatchPathOverride ?? appItem?.PatchPathOverride) ?? defaultPatchPath;
                var patchCtx = VitaPatchShared.GetPatchContext(patchContexts, patchPackagePath, log);
                var index = VitaPatchShared.BuildPatchAppIndex(appOwner, patchOwner);
                var targets = VitaPatchShared.BuildTargets(index, patchCtx);

                groups.Add(new MergeGroup { Category = VitaContentCategory.App, TitleId = titleId, PatchCtx = patchCtx, Index = index, Targets = targets, Owners = owners });
            }

            foreach (var addcontItem in addcontItems)
            {
                ct.ThrowIfCancellationRequested();

                var owner = VitaPatchShared.BuildOwnerContext(addcontItem, null, log);

                if (owner is null)
                    continue;

                string patchPackagePath = addcontItem.PatchPathOverride ?? defaultPatchPath;
                var patchCtx = VitaPatchShared.GetPatchContext(patchContexts, patchPackagePath, log);
                var index = VitaPatchShared.BuildPatchAppIndex(owner, null);
                var targets = VitaPatchShared.BuildTargets(index, patchCtx);

                groups.Add(new MergeGroup { Category = VitaContentCategory.Addcont, TitleId = addcontItem.TitleId, ContentIdSuffix = addcontItem.ContentIdSuffix, PatchCtx = patchCtx, Index = index, Targets = targets, Owners = [owner] });
            }

            long patchTotal = groups.Sum(g => g.Targets.Sum(t => t.EstimatedSize));
            var patchReporter = new ProgressReporter("패치 적용 중", string.Empty, patchTotal, progress);
            var resolved = new Dictionary<(MergeGroup Group, string RelativePath), ResolvedTarget>();
            int patchCandidates = 0;
            int patchedSuccess = 0;

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var t in group.Targets)
                {
                    ct.ThrowIfCancellationRequested();

                    patchCandidates++;

                    try
                    {
                        byte[] bytes = await VitaPatchShared.ResolveTargetBytesAsync(t, group.PatchCtx, log, ct);

                        resolved[(group, t.RelativePath)] = new ResolvedTarget(bytes, t.EstimatedSize);
                        patchedSuccess++;
                    }
                    catch (Exception ex)
                    {
                        log($"[{group.Category}] {t.RelativePath}: 패치 실패, 원본 유지 - {ex.Message}", LogLevel.Error);
                    }

                    patchReporter.AddProgress(t.EstimatedSize);
                }
            }

            patchReporter.ForceReport();

            long zipTotal = 0;

            foreach (var group in groups)
            {
                if (target == VitaOutputTarget.Emu)
                {
                    foreach (var appEntry in group.Index.Values)
                        zipTotal += appEntry.FileEntry.Size;
                }
                else
                {
                    foreach (var owner in group.Owners)
                    {
                        foreach (var (_, size) in VitaPatchShared.GetAllOwnedFiles(owner))
                            zipTotal += size;
                    }

                    foreach (var t in group.Targets)
                    {
                        if (resolved.ContainsKey((group, t.RelativePath)))
                            zipTotal += t.EstimatedSize;
                    }
                }
            }

            var zipReporter = new ProgressReporter("압축 중", string.Empty, zipTotal, progress);
            var writtenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);

            using (var zipStream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var group in groups)
                {
                    ct.ThrowIfCancellationRequested();

                    if (target == VitaOutputTarget.Emu)
                    {
                        foreach (var (relativePath, appEntry) in group.Index)
                        {
                            ct.ThrowIfCancellationRequested();

                            bool isTarget = resolved.TryGetValue((group, relativePath), out var resolvedTarget);
                            byte[] outputBytes;

                            if (isTarget)
                                outputBytes = resolvedTarget!.Bytes;
                            else
                            {
                                try
                                {
                                    outputBytes = VitaNoNpDrmDecryptor.DecryptEntry(appEntry.Owner.Item.Accessor!, appEntry.Owner.Item.SourcePath, appEntry.Owner.License.Klicensee, appEntry.FileEntry, appEntry.Owner.Table.UnicvEntries[appEntry.EntryIndex], appEntry.Owner.Table.FilesSalt, out string? warning);

                                    if (warning != null)
                                        log($"[{group.Category}] {warning}", LogLevel.Highlight);
                                }
                                catch (Exception ex)
                                {
                                    log($"[{group.Category}] {relativePath}: 복호화 실패 - {ex.Message}", LogLevel.Error);
                                    continue;
                                }
                            }

                            string entryPath = BuildEntryPath(VitaPatchShared.GetBasePrefix(group.Category), group, relativePath);

                            await WriteZipEntryAsync(zip, writtenEntries, entryPath, outputBytes, appEntry.FileEntry.Size, zipReporter, log, group.Category, relativePath, ct);
                        }
                    }
                    else
                    {
                        foreach (var owner in group.Owners)
                        {
                            foreach (var (relativePath, size) in VitaPatchShared.GetAllOwnedFiles(owner))
                            {
                                ct.ThrowIfCancellationRequested();

                                string srcRel = $"{owner.Item.SourcePath}/{relativePath}";
                                byte[] rawBytes;

                                try
                                {
                                    rawBytes = owner.Item.Accessor!.ReadAllBytes(srcRel);
                                }
                                catch (Exception ex)
                                {
                                    log($"[{owner.Item.Category}] {relativePath}: 원본 읽기 실패 - {ex.Message}", LogLevel.Error);
                                    continue;
                                }

                                string baseEntryPath = BuildEntryPath(VitaPatchShared.GetRetailBaseFolder(owner.Item.Category), group, relativePath);

                                await WriteZipEntryAsync(zip, writtenEntries, baseEntryPath, rawBytes, size, zipReporter, log, owner.Item.Category, relativePath, ct);

                                bool isEffectiveOwner = group.Index.TryGetValue(relativePath, out var winner) && winner.Owner == owner;

                                if (isEffectiveOwner && resolved.TryGetValue((group, relativePath), out var resolvedTarget))
                                {
                                    string patchedEntryPath = BuildEntryPath(VitaPatchShared.GetPatchedPrefix(group.Category, target), group, relativePath);

                                    await WriteZipEntryAsync(zip, writtenEntries, patchedEntryPath, resolvedTarget.Bytes, resolvedTarget.EstimatedSize, zipReporter, log, group.Category, relativePath, ct);
                                }
                            }
                        }
                    }

                    if (target == VitaOutputTarget.Emu)
                    {
                        foreach (var owner in group.Owners)
                            VitaPatchShared.WriteLicenseEntry(zip, writtenEntries, owner);
                    }
                }

                zipReporter.ForceReport();
            }

            return new VitaMergeResult { TotalFiles = writtenEntries.Count, PatchCandidates = patchCandidates, PatchedSuccessfully = patchedSuccess };
        }
        finally
        {
            foreach (var patchCtx in patchContexts.Values)
                patchCtx.Accessor.Dispose();
        }
    }

    private static string BuildEntryPath(string prefix, MergeGroup group, string relativePath) =>
        group.Category == VitaContentCategory.Addcont ? VitaPatchShared.NormalizeZipPath($"{prefix}/{group.TitleId}/{group.ContentIdSuffix}/{relativePath}") : VitaPatchShared.NormalizeZipPath($"{prefix}/{group.TitleId}/{relativePath}");

    private static async Task WriteZipEntryAsync(ZipArchive zip, HashSet<string> writtenEntries, string entryPath, byte[] data, long estimatedSize, ProgressReporter reporter, Action<string, LogLevel> log, VitaContentCategory category, string relativePath, CancellationToken ct)
    {
        if (!writtenEntries.Add(entryPath))
        {
            log($"[{category}] {relativePath}: 이미 같은 경로로 추가된 항목이라 건너뜀 (중복)", LogLevel.Highlight);
            return;
        }

        var zipEntry = zip.CreateEntry(entryPath, CompressionLevel.NoCompression);

        await VitaPatchShared.WriteEntryWithProgressAsync(zipEntry, data, estimatedSize, reporter, ct);
    }
}