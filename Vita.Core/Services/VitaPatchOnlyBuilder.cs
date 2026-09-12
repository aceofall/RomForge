using Patch.Core;
using System.IO.Compression;
using Vita.Core.Models;

namespace Vita.Core.Services;

public static class VitaPatchOnlyBuilder
{
    private static readonly HashSet<string> PatchExtensions = new(StringComparer.OrdinalIgnoreCase) { ".xdelta", ".xdelta3", ".ips", ".ups", ".bps", ".ppf", ".aps" };

    public static async Task<VitaPatchOnlyResult> BuildAsync(string sourcePath, string patchPath, string outputZipPath, VitaOutputTarget target, Action<string> log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var source = VitaSourceAccessorFactory.Open(sourcePath);
        using var patch = VitaSourceAccessorFactory.Open(patchPath);
        var allPatchFiles = patch.EnumerateAllFiles().ToList();
        var patchFiles = BuildPatchFileMap(allPatchFiles, log);
        var rawOverwriteFiles = allPatchFiles
            .Where(f => !PatchExtensions.Contains(Path.GetExtension(f)))
            .ToList();
        var items = VitaSourcePreparer.DiscoverItems(source);
        int matched = 0;
        int success = 0;

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
                patchOwnedRelativePaths = TryGetOwnedPaths(source, patchItem, log);

                log($"[patch] {patchItem.TitleId}: 이 폴더가 소유한 파일 {patchOwnedRelativePaths.Count}개 확인됨 (app에서는 스킵)");

                string? appWorkBinRel = null;

                if (appByTitle.TryGetValue(titleId, out var appItemForWorkBin))
                {
                    string candidateWorkBin = $"{appItemForWorkBin.SourcePath}/sce_sys/package/work.bin";

                    if (source.FileExists(candidateWorkBin))
                        appWorkBinRel = candidateWorkBin;
                }

                var r = await ProcessItemAsync(source, patch, patchItem, patchFiles, rawOverwriteFiles, target, zip, null, appWorkBinRel, log, ct, progress);

                matched += r.matched;
                success += r.success;
            }

            if (appByTitle.TryGetValue(titleId, out var appItem))
            {
                var r = await ProcessItemAsync(source, patch, appItem, patchFiles, rawOverwriteFiles, target, zip, patchOwnedRelativePaths, null, log, ct, progress);

                matched += r.matched;
                success += r.success;
            }
        }

        foreach (var addcontItem in addcontItems)
        {
            ct.ThrowIfCancellationRequested();

            var r = await ProcessItemAsync(source, patch, addcontItem, patchFiles, rawOverwriteFiles, target, zip, null, null, log, ct, progress);

            matched += r.matched;
            success += r.success;
        }

        return new VitaPatchOnlyResult { MatchedCandidates = matched, PatchedSuccessfully = success };
    }

    public static async Task<VitaPatchOnlyResult> BuildFromPkgAsync(string pkgPath, string license, string patchPath, string outputZipPath, VitaOutputTarget target, Action<string> log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var source = new PkgSourceAccessor(pkgPath, license);
        using var patch = VitaSourceAccessorFactory.Open(patchPath);
        var allPatchFiles = patch.EnumerateAllFiles().ToList();
        var patchFiles = BuildPatchFileMap(allPatchFiles, log);
        var rawOverwriteFiles = allPatchFiles
            .Where(f => !PatchExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (!WorkBinReader.TryGetTitleIdFromContentId(source.Header.ContentId, out string titleId))
            throw new InvalidDataException($"content_id에서 title id를 추출할 수 없습니다: {source.Header.ContentId}");

        VitaSourceItem item = source.Header.ContentType switch
        {
            (uint)VitaContentType.App => new VitaSourceItem { Category = VitaPkgCategoryResolver.ResolveAppOrPatch(source), TitleId = titleId, SourcePath = string.Empty },
            (uint)VitaContentType.Dlc => new VitaSourceItem { Category = VitaContentCategory.Addcont, TitleId = titleId, ContentIdSuffix = source.Header.ContentId.Length > 20 ? source.Header.ContentId[20..] : source.Header.ContentId, SourcePath = string.Empty },
            _ => throw new NotSupportedException($"지원하지 않는 PKG content type: 0x{source.Header.ContentType:x}")
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);

        using var zipStream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var r = await ProcessItemAsync(source, patch, item, patchFiles, rawOverwriteFiles, target, zip, null, null, log, ct, progress);

        return new VitaPatchOnlyResult { MatchedCandidates = r.matched, PatchedSuccessfully = r.success };
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

    private static HashSet<string> TryGetOwnedPaths(IVitaSourceAccessor source, VitaSourceItem item, Action<string> log)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var table = VitaNoNpDrmDecryptor.ParseFileTable(source, item.SourcePath);

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

    private static async Task<(int matched, int success)> ProcessItemAsync(IVitaSourceAccessor source, IVitaSourceAccessor patch, VitaSourceItem item, Dictionary<string, string> patchFiles, List<string> rawOverwriteFiles, VitaOutputTarget target,
        ZipArchive zip, HashSet<string>? skipRelativePaths, string? fallbackWorkBinRel, Action<string> log, CancellationToken ct, IProgress<double>? progress)
    {
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
                byte[] sourceBytes = VitaNoNpDrmDecryptor.DecryptEntry(source, item.SourcePath, license.Klicensee, entry, table.UnicvEntries[i], table.FilesSalt, out string? warning);

                if (warning != null)
                    log($"[{item.Category}] {warning}");

                byte[] patchBytes = patch.ReadAllBytes(patchFileRel);
                byte[] patchedBytes = await UniversalPatcher.ApplyPatchAsync(sourceBytes, patchBytes, ct: ct);
                string prefix = GetPrefix(item.Category, target);
                string entryPath = item.Category == VitaContentCategory.Addcont ? $"{prefix}/{item.TitleId}/{item.ContentIdSuffix}/{relativePath}" : $"{prefix}/{item.TitleId}/{relativePath}";
                var zipEntry = zip.CreateEntry(entryPath.Replace('\\', '/'), CompressionLevel.Optimal);

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
                byte[] rawBytes = patch.ReadAllBytes(rawRel);
                string prefix = GetPrefix(item.Category, target);
                string entryPath = item.Category == VitaContentCategory.Addcont ? $"{prefix}/{item.TitleId}/{item.ContentIdSuffix}/{normalizedRawRel}" : $"{prefix}/{item.TitleId}/{normalizedRawRel}";
                var zipEntry = zip.CreateEntry(entryPath.Replace('\\', '/'), CompressionLevel.Optimal);

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

        if (target == VitaOutputTarget.Emu && WorkBinReader.TryGetTitleIdFromContentId(license.ContentId, out string licenseTitleId))
        {
            var licenseEntry = zip.CreateEntry($"license/{licenseTitleId}/{license.ContentId}.rif", CompressionLevel.Optimal);
            byte[] workBinBytes = source.ReadAllBytes(workBinRel);
            using var es = licenseEntry.Open();

            await es.WriteAsync(workBinBytes, ct);
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