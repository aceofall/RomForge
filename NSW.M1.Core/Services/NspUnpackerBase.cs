using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.NSZ;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using NSW.Core;
using NSW.M1.Core.Models;
using System.Diagnostics;
using ContentType = LibHac.Ncm.ContentType;
using Path = System.IO.Path;

namespace NSW.M1.Core.Services;

public abstract class NspUnpackerBase(KeySet keySet)
{
    protected readonly KeySet _keySet = keySet;
    private readonly List<Stream> _nscStreams = [];

    public string BaseNspPath { get; private set; } = string.Empty;

    protected UnpackResult UnpackCore(BuildRequest req, string outDir, bool withControlNca, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        BaseNspPath = req.BaseFilePath;
        Directory.CreateDirectory(outDir);

        var storages = new List<LocalStorage>();
        var partitions = new List<IFileSystem>();

        try
        {
            var allPaths = new List<string> { req.BaseFilePath };
            if (!string.IsNullOrEmpty(req.UpdateFilePath)) allPaths.Add(req.UpdateFilePath);
            allPaths.AddRange(req.DlcFilePaths);

            var uniquePaths = allPaths.Distinct().Where(File.Exists).ToList();

            foreach (var path in uniquePaths)
            {
                var storage = new LocalStorage(path, FileAccess.Read);
                storages.Add(storage);
                var pfs = storage.OpenFileSystem(_keySet, path);
                partitions.Add(pfs);
                _keySet.RegisterTickets(pfs);
            }

            var ncas = CollectAllNcas(partitions);

            if (!ncas.BaseProgs.ContainsKey(0))
                throw new Exception("Base Program NCA를 찾을 수 없습니다.");

            var progCtx = CreateProgressContext(uniquePaths, ncas);
            var result = BuildBaseResult(ncas);

            var allOffsets = ncas.BaseProgs.Keys
                .Union(ncas.UpdateProgs.Keys)
                .OrderBy(k => k);

            foreach (var idOffset in allOffsets)
            {
                ncas.BaseProgs.TryGetValue(idOffset, out var baseProg);
                ncas.UpdateProgs.TryGetValue(idOffset, out var updateProg);

                if (baseProg == null && updateProg == null) 
                    continue;

                string exefsName = idOffset == 0 ? "exefs" : $"exefs{idOffset}";
                string romfsName = idOffset == 0 ? "romfs" : $"romfs{idOffset}";
                string logoName = idOffset == 0 ? "logo" : $"logo{idOffset}";
                string controlName = idOffset == 0 ? "control" : $"control{idOffset}";
                string htmlName = idOffset == 0 ? "htmldoc" : $"htmldoc{idOffset}";
                string legalName = idOffset == 0 ? "legal" : $"legal{idOffset}";

                var effectiveProg = baseProg ?? updateProg!;
                var patchProg = baseProg != null ? updateProg : null;

                if (effectiveProg.CanOpenSection(NcaSectionType.Code))
                    result.ExefsDirs[idOffset] = ExtractExeFs(effectiveProg, patchProg, exefsName, outDir, progCtx, progress, ct);

                if (effectiveProg.CanOpenSection(NcaSectionType.Data))
                    result.RomfsDirs[idOffset] = ExtractRomFs(effectiveProg, patchProg, romfsName, outDir, progCtx, progress, ct, ncas.CreateOnlyOffsets.Contains(idOffset));

                var logoDir = ExtractLogo(effectiveProg, logoName, outDir, progCtx, progress, ct);

                if (logoDir != null) 
                    result.LogoDirs[idOffset] = logoDir;

                ncas.BaseControls.TryGetValue(idOffset, out var baseControl);
                ncas.UpdateControls.TryGetValue(idOffset, out var updateControl);

                var effectiveControl = baseControl ?? updateControl;
                var controlDir = ExtractControl(effectiveControl, baseControl != null ? updateControl : null, req, result, controlName, outDir, progCtx, progress, ct);

                if (controlDir != null) 
                    result.ControlDirs[idOffset] = controlDir;

                ncas.BaseHtmls.TryGetValue(idOffset, out var baseHtml);
                ncas.UpdateHtmls.TryGetValue(idOffset, out var updateHtml);
                var htmlDir = ExtractHtmlDoc(baseHtml, updateHtml, htmlName, outDir, progCtx, progress, ct);

                if (htmlDir != null) 
                    result.HtmlDocDirs[idOffset] = htmlDir;

                ncas.BaseLegals.TryGetValue(idOffset, out var baseLegal);
                ncas.UpdateLegals.TryGetValue(idOffset, out var updateLegal);
                var legalDir = ExtractLegal(baseLegal, updateLegal, legalName, outDir, progCtx, progress, ct);

                if (legalDir != null) 
                    result.LegalDirs[idOffset] = legalDir;
            }

            ExtractDlcs(ncas.DlcNcas, outDir, result, progCtx, progress, ct);

            if (withControlNca)
            {
                if (ncas.BaseControls.TryGetValue(0, out var mainControl))
                {
                    uint version = ncas.PatchVersion;
                    string fileName = version > 0 ? $"control_{version}.nca" : "control.nca";
                    string controlNcaPath = Path.Combine(outDir, fileName);
                    using var fs = new FileStream(controlNcaPath, FileMode.Create, FileAccess.Write);
                    mainControl.BaseStorage.CopyToStream(fs);
                }
            }

            progress?.Report((100, "완료"));

            return result;
        }
        finally
        {
            foreach (var s in storages) s.Dispose();
            foreach (var ns in _nscStreams) ns.Dispose();
            _nscStreams.Clear();
        }
    }

    protected abstract ProgressContext CreateProgressContext(List<string> inputPaths, CollectedNcas ncas);

    protected abstract bool ShouldExtract(string entryPath);

    protected sealed record CollectedNcas(
        Dictionary<byte, Nca> BaseProgs,
        Dictionary<byte, Nca> UpdateProgs,
        Dictionary<byte, Nca> BaseControls,
        Dictionary<byte, Nca> UpdateControls,
        Dictionary<byte, Nca> BaseHtmls,
        Dictionary<byte, Nca> UpdateHtmls,
        Dictionary<byte, Nca> BaseLegals,
        Dictionary<byte, Nca> UpdateLegals,
        List<Nca> DlcNcas,
        uint PatchVersion,
        HashSet<byte> CreateOnlyOffsets);

    private CollectedNcas CollectAllNcas(List<IFileSystem> partitions)
    {
        var baseProgIds = new Dictionary<byte, string>();
        var updateProgIds = new Dictionary<byte, string>();
        var baseControlIds = new Dictionary<byte, string>();
        var updateControlIds = new Dictionary<byte, string>();
        var baseHtmlIds = new Dictionary<byte, string>();
        var updateHtmlIds = new Dictionary<byte, string>();
        var baseLegalIds = new Dictionary<byte, string>();
        var updateLegalIds = new Dictionary<byte, string>();
        var dlcNcaIds = new HashSet<string>();
        uint patchVersion = 0;
        var deltaToBaseNcaId = new Dictionary<string, string>();
        var createNcaIds = new HashSet<string>();
        var createOnlyOffsets = new HashSet<byte>();

        foreach (var cnmt in EnumerateCnmts(partitions))
        {
            if (cnmt.Type == ContentMetaType.Patch)
            {
                patchVersion = cnmt.TitleVersion.Version;

                if (cnmt.ExtendedData?.FragmentSets != null)
                {
                    foreach (var fs in cnmt.ExtendedData.FragmentSets)
                    {
                        if (fs.Type != ContentType.Program) 
                            continue;

                        string newId = Convert.ToHexString(fs.NcaIdNew).ToLowerInvariant();
                        string oldId = Convert.ToHexString(fs.NcaIdOld).ToLowerInvariant();

                        if (fs.DeltaType == UpdateType.Create)
                            createNcaIds.Add(newId);
                        else
                            deltaToBaseNcaId[newId] = oldId;
                    }
                }
            }

            foreach (var entry in cnmt.ContentEntries)
            {
                string ncaId = Convert.ToHexString(entry.NcaId).ToLowerInvariant();
                byte idOffset = entry.IdOffset;

                switch (cnmt.Type)
                {
                    case ContentMetaType.Application:
                        switch (entry.Type)
                        {
                            case ContentType.Program: baseProgIds[idOffset] = ncaId; break;
                            case ContentType.Control: baseControlIds[idOffset] = ncaId; break;
                            case ContentType.HtmlDocument: baseHtmlIds[idOffset] = ncaId; break;
                            case ContentType.LegalInformation: baseLegalIds[idOffset] = ncaId; break;
                        }
                        break;
                    case ContentMetaType.Patch:
                        switch (entry.Type)
                        {
                            case ContentType.Program: updateProgIds[idOffset] = ncaId; break;
                            case ContentType.Control: updateControlIds[idOffset] = ncaId; break;
                            case ContentType.HtmlDocument: updateHtmlIds[idOffset] = ncaId; break;
                            case ContentType.LegalInformation: updateLegalIds[idOffset] = ncaId; break;
                        }
                        break;
                    case ContentMetaType.AddOnContent:
                        dlcNcaIds.Add(ncaId);
                        break;
                }
            }
        }

        var baseProgReverse = baseProgIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var updateProgReverse = updateProgIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var baseControlReverse = baseControlIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var updateControlReverse = updateControlIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var baseHtmlReverse = baseHtmlIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var updateHtmlReverse = updateHtmlIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var baseLegalReverse = baseLegalIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var updateLegalReverse = updateLegalIds.ToDictionary(kv => kv.Value, kv => kv.Key);

        var baseProgs = new Dictionary<byte, Nca>();
        var updateProgs = new Dictionary<byte, Nca>();
        var baseControls = new Dictionary<byte, Nca>();
        var updateControls = new Dictionary<byte, Nca>();
        var baseHtmls = new Dictionary<byte, Nca>();
        var updateHtmls = new Dictionary<byte, Nca>();
        var baseLegals = new Dictionary<byte, Nca>();
        var updateLegals = new Dictionary<byte, Nca>();
        var dlcNcas = new List<Nca>();
        var processedIds = new HashSet<string>();

        var allNcas = new Dictionary<string, Nca>();

        foreach (var pfs in partitions)
        {
            foreach (var entry in EnumerateNcaEntries(pfs))
            {
                string currentId = Path.GetFileNameWithoutExtension(entry.Name).ToLower();

                if (!processedIds.Add(currentId)) 
                    continue;

                var nca = OpenNca(pfs, entry.FullPath);

                if (nca == null) 
                    continue;

                allNcas[currentId] = nca;
            }
        }

        foreach (var (currentId, nca) in allNcas)
        {
            if (baseProgReverse.TryGetValue(currentId, out var bpOffset))
                baseProgs[bpOffset] = nca;
            else if (updateProgReverse.TryGetValue(currentId, out var upOffset))
            {
                if (createNcaIds.Contains(currentId))
                {
                    baseProgs[upOffset] = nca;
                    createOnlyOffsets.Add(upOffset);
                }
                else if (deltaToBaseNcaId.TryGetValue(currentId, out var targetBaseId) && baseProgReverse.TryGetValue(targetBaseId, out var realBaseOffset))
                    updateProgs[realBaseOffset] = nca;
                else
                    updateProgs[upOffset] = nca;
            }
            else if (baseControlReverse.TryGetValue(currentId, out var bcOffset))
                baseControls[bcOffset] = nca;
            else if (updateControlReverse.TryGetValue(currentId, out var ucOffset))
                updateControls[ucOffset] = nca;
            else if (baseHtmlReverse.TryGetValue(currentId, out var bhOffset))
                baseHtmls[bhOffset] = nca;
            else if (updateHtmlReverse.TryGetValue(currentId, out var uhOffset))
                updateHtmls[uhOffset] = nca;
            else if (baseLegalReverse.TryGetValue(currentId, out var blOffset))
                baseLegals[blOffset] = nca;
            else if (updateLegalReverse.TryGetValue(currentId, out var ulOffset))
                updateLegals[ulOffset] = nca;
            else if (dlcNcaIds.Contains(currentId))
                dlcNcas.Add(nca);
        }

        return new CollectedNcas(baseProgs, updateProgs, baseControls, updateControls, baseHtmls, updateHtmls, baseLegals, updateLegals, dlcNcas, patchVersion, createOnlyOffsets);
    }

    private static UnpackResult BuildBaseResult(CollectedNcas ncas)
    {
        if (!ncas.BaseControls.TryGetValue(0, out var mainControl))
            throw new Exception("Control NCA를 찾을 수 없습니다.");

        var result = new UnpackResult
        {
            TitleId = mainControl.Header.TitleId,
            BaseSdkVersion = mainControl.Header.SdkVersion.Version,
            BaseKeyGeneration = mainControl.Header.KeyGeneration,
        };

        if (ncas.UpdateProgs.ContainsKey(0))
        {
            result.HasUpdate = true;
            result.GameVersion = ncas.PatchVersion;
        }

        return result;
    }

    private string ExtractExeFs(Nca baseProg, Nca? updateProg, string dirName, string outDir, ProgressContext ctx, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        string dir = Path.Combine(outDir, dirName);
        using var fs = updateProg != null ? baseProg.OpenFileSystemWithPatch(updateProg, NcaSectionType.Code, IntegrityCheckLevel.None) : baseProg.OpenFileSystem(NcaSectionType.Code, IntegrityCheckLevel.None);

        ExtractFileSystem(fs, dir, ctx, progress, "ExeFS", ct);

        return dir;
    }

    private string ExtractRomFs(Nca baseProg, Nca? updateProg, string dirName, string outDir, ProgressContext ctx, IProgress<(int pct, string label)>? progress, CancellationToken ct, bool isCreateOnly = false)
    {
        string dir = Path.Combine(outDir, dirName);

        if (!isCreateOnly)
        {
            bool hasSparse = false;

            for (int i = 0; i < 4; i++)
            {
                if (!baseProg.SectionExists(i)) 
                    continue;

                if (baseProg.GetFsHeader(i).ExistsSparseLayer()) 
                { 
                    hasSparse = true; 
                    break;
                }
            }
            if (hasSparse && updateProg == null)
                throw new InvalidOperationException("SparseStorage NCA는 업데이트가 필요합니다.");
        }

        using var fs = updateProg != null ? baseProg.OpenFileSystemWithPatch(updateProg, NcaSectionType.Data, IntegrityCheckLevel.None) : baseProg.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None);

        ExtractFileSystem(fs, dir, ctx, progress, "RomFS", ct);

        return dir;
    }

    private string? ExtractLogo(Nca baseProg, string dirName, string outDir, ProgressContext ctx, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        if (!baseProg.CanOpenSection(NcaSectionType.Logo)) 
            return null;

        string dir = Path.Combine(outDir, dirName);
        using var fs = baseProg.OpenFileSystem(NcaSectionType.Logo, IntegrityCheckLevel.None);

        ExtractFileSystem(fs, dir, ctx, progress, "Logo", ct);

        return dir;
    }

    private string? ExtractControl(Nca? controlNca, Nca? updateControlNca, BuildRequest req, UnpackResult result, string dirName, string outDir, ProgressContext ctx, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        if (controlNca == null) 
            return null;

        string dir = Path.Combine(outDir, dirName);

        using (var fs = controlNca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None))
            ExtractFileSystem(fs, dir, ctx, progress, "Control (Base)", ct);

        if (updateControlNca != null)
        {
            using var updateFs = updateControlNca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None);
            ExtractFileSystem(updateFs, dir, ctx, progress, "Control (Update)", ct);
        }

        string metaSourcePath = !string.IsNullOrEmpty(req.UpdateFilePath) ? req.UpdateFilePath : BaseNspPath;
        var meta = MetadataReader.GetMetadataFromContainer(_keySet, metaSourcePath)
            .OrderByDescending(m => m.Type == ContentMetaType.Patch)
            .FirstOrDefault();

        if (meta != null)
        {
            result.KrTitle = meta.KrTitle;
            result.EnTitle = meta.EnTitle;
            result.DisplayVersion = meta.DisplayVersion;
        }

        return dir;
    }

    private string? ExtractHtmlDoc(Nca? baseHtml, Nca? updateHtml, string dirName, string outDir, ProgressContext ctx, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        if (baseHtml == null && updateHtml == null) 
            return null;

        string dir = Path.Combine(outDir, dirName);

        try
        {
            using var fs = baseHtml != null && updateHtml != null
                ? baseHtml.OpenFileSystemWithPatch(updateHtml, NcaSectionType.Data, IntegrityCheckLevel.None)
                : (baseHtml ?? updateHtml)!.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None);

            ExtractFileSystem(fs, dir, ctx, progress, "HtmlDoc", ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HtmlDoc 추출 실패 ({dirName}): {ex.Message}");

            return null;
        }
        return dir;
    }

    private string? ExtractLegal(Nca? baseLegal, Nca? updateLegal, string dirName, string outDir, ProgressContext ctx, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        if (baseLegal == null && updateLegal == null)
            return null;

        string dir = Path.Combine(outDir, dirName);
        using var fs = baseLegal != null && updateLegal != null
            ? baseLegal.OpenFileSystemWithPatch(updateLegal, NcaSectionType.Data, IntegrityCheckLevel.None)
            : (baseLegal ?? updateLegal)!.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None);

        ExtractFileSystem(fs, dir, ctx, progress, "Legal", ct);

        return dir;
    }

    private void ExtractDlcs(List<Nca> dlcNcas, string outDir, UnpackResult result, ProgressContext ctx, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        if (dlcNcas.Count == 0) 
            return;

        string dlcBaseDir = Path.Combine(outDir, "DLCs");

        foreach (var nca in dlcNcas)
        {
            ct.ThrowIfCancellationRequested();

            string titleIdStr = nca.Header.TitleId.ToString("X16");
            string dlcDestDir = Path.Combine(dlcBaseDir, titleIdStr, "romfs");

            if (!nca.CanOpenSection(NcaSectionType.Data)) 
                continue;

            try
            {
                using var fs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None);

                ExtractFileSystem(fs, dlcDestDir, ctx, progress, $"DLC ({titleIdStr})", ct);

                result.Dlcs.Add(new DlcUnpackInfo { TitleId = nca.Header.TitleId, Dir = Path.Combine("DLCs", titleIdStr) });
            }
            catch { }
        }
    }

    private void ExtractFileSystem(IFileSystem fs, string outDir, ProgressContext ctx, IProgress<(int pct, string label)>? progress, string label, CancellationToken ct)
    {
        var buf = new byte[0x1000000];

        foreach (var entry in fs.EnumerateEntries("/", "*", SearchOptions.RecurseSubdirectories))
        {
            ct.ThrowIfCancellationRequested();

            if (!ShouldExtract(entry.FullPath)) 
                continue;

            string destPath = Path.Combine(outDir, entry.FullPath.TrimStart('/'));

            if (entry.Type == DirectoryEntryType.Directory)
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using var src = new UniqueRef<IFile>();

            if (!fs.OpenFile(ref src.Ref, entry.FullPath.ToU8Span(), OpenMode.Read).IsSuccess()) 
                continue;

            using var dest = File.Open(destPath, FileMode.Create);
            var srcStream = src.Get.AsStream();
            int read;

            while ((read = srcStream.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                dest.Write(buf, 0, read);
                ctx.CurrentRead += read;

                if (progress != null)
                {
                    var now = DateTime.UtcNow;
                    if ((now - ctx.LastReport).TotalSeconds >= 0.2)
                    {
                        ctx.LastReport = now;
                        int percent = Math.Min((int)((double)ctx.CurrentRead / ctx.TotalSize * 100), 99);
                        progress.Report((percent, $"{label}: {entry.Name}"));
                    }
                }
            }
        }
    }

    protected long CalculateMatchedSize(CollectedNcas ncas)
    {
        long total = 0;

        void SumFs(IFileSystem fs)
        {
            foreach (var entry in fs.EnumerateEntries("/", "*", SearchOptions.RecurseSubdirectories))
            {
                if (entry.Type == DirectoryEntryType.File && ShouldExtract(entry.FullPath))
                    total += entry.Size;
            }
        }

        foreach (var (idOffset, baseProg) in ncas.BaseProgs)
        {
            ncas.UpdateProgs.TryGetValue(idOffset, out var updateProg);

            TryWithFs(baseProg, updateProg, NcaSectionType.Code, SumFs);
            TryWithFs(baseProg, updateProg, NcaSectionType.Data, SumFs);

            if (idOffset == 0 && baseProg.CanOpenSection(NcaSectionType.Logo))
                TryWithFs(baseProg, null, NcaSectionType.Logo, SumFs);
        }

        foreach (var (idOffset, baseControl) in ncas.BaseControls)
        {
            ncas.UpdateControls.TryGetValue(idOffset, out var updateControl);

            TryWithFs(baseControl, updateControl, NcaSectionType.Data, SumFs);
        }

        foreach (var (idOffset, baseHtml) in ncas.BaseHtmls)
        {
            ncas.UpdateHtmls.TryGetValue(idOffset, out var updateHtml);

            TryWithFs(baseHtml, updateHtml, NcaSectionType.Data, SumFs);
        }

        foreach (var (idOffset, baseLegal) in ncas.BaseLegals)
        {
            ncas.UpdateLegals.TryGetValue(idOffset, out var updateLegal);

            TryWithFs(baseLegal, updateLegal, NcaSectionType.Data, SumFs);
        }

        foreach (var dlc in ncas.DlcNcas)
            TryWithFs(dlc, null, NcaSectionType.Data, SumFs);

        return total;
    }

    protected static void TryWithFs(Nca? nca, Nca? patchNca, NcaSectionType section, Action<IFileSystem> action)
    {
        if (nca == null) 
            return;

        try
        {
            using var fs = patchNca != null
                ? nca.OpenFileSystemWithPatch(patchNca, section, IntegrityCheckLevel.None)
                : nca.OpenFileSystem(section, IntegrityCheckLevel.None);

            action(fs);
        }
        catch { }
    }

    private IEnumerable<LibHac.Tools.Ncm.Cnmt> EnumerateCnmts(IEnumerable<IFileSystem> partitions)
    {
        foreach (var pfs in partitions)
        {
            foreach (var entry in EnumerateNcaEntries(pfs))
            {
                LibHac.Tools.Ncm.Cnmt[]? cnmts = null;

                try
                {
                    var metaNca = OpenNca(pfs, entry.FullPath);

                    if (metaNca?.Header.ContentType != NcaContentType.Meta) 
                        continue;

                    using var metaFs = metaNca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None);

                    cnmts = [.. ReadCnmtsFromFs(metaFs)];
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CNMT 파싱 실패 ({entry.FullPath}): {ex.Message}");
                    continue;
                }

                if (cnmts == null) 
                    continue;

                foreach (var c in cnmts) 
                    yield return c;
            }
        }
    }

    private static IEnumerable<LibHac.Tools.Ncm.Cnmt> ReadCnmtsFromFs(IFileSystem metaFs)
    {
        foreach (var cnmtEntry in metaFs.EnumerateEntries("/", "*.cnmt"))
        {
            using var cnmtFile = new UniqueRef<IFile>();

            if (metaFs.OpenFile(ref cnmtFile.Ref, cnmtEntry.FullPath.ToU8Span(), OpenMode.Read).IsFailure()) 
                continue;

            yield return new LibHac.Tools.Ncm.Cnmt(cnmtFile.Get.AsStream());
        }
    }

    private static IEnumerable<DirectoryEntryEx> EnumerateNcaEntries(IFileSystem pfs) =>
        pfs.EnumerateEntries("/", "*.nc*")
           .Where(e => e.Type == DirectoryEntryType.File &&
                      (e.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase) ||
                       e.Name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase)));

    private Nca? OpenNca(IFileSystem pfs, string fullPath)
    {
        try
        {
            var file = new UniqueRef<IFile>();

            if (pfs.OpenFile(ref file.Ref, fullPath.ToU8Span(), OpenMode.Read).IsFailure())
                return null;

            Stream baseStream = file.Release().AsStream();
            _nscStreams.Add(baseStream);

            if (fullPath.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase))
            {
                var ncz = new Ncz(_keySet, baseStream, NczReadMode.Fast);
                return ncz;
            }
            else
                return new Nca(_keySet, baseStream.AsStorage());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NCA 열기 실패 ({fullPath}): {ex.Message}");

            return null;
        }
    }
}