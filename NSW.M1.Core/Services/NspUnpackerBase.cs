using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using NSW.Core;
using NSW.M1.Core.Models;
using Path = System.IO.Path;

namespace NSW.M1.Core.Services;

public abstract class NspUnpackerBase(KeySet keySet)
{
    protected readonly KeySet _keySet = keySet;

    public string BaseNspPath { get; private set; } = string.Empty;

    protected UnpackResult UnpackCore(BuildRequest req, string outDir, bool withControlNca, IProgress<(int pct, string label)>? progress, CancellationToken ct)
    {
        BaseNspPath = req.BaseFilePath;
        Directory.CreateDirectory(outDir);

        var storages = new List<LocalStorage>();
        var partitions = new List<IFileSystem>();
        var collector = new NcaCollector(_keySet);

        try
        {
            var allPaths = new List<string> { req.BaseFilePath };

            if (!string.IsNullOrEmpty(req.UpdateFilePath))
                allPaths.Add(req.UpdateFilePath);

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

            var ncas = collector.Collect(partitions);

            if (!ncas.BaseProgs.ContainsKey(0))
                throw new Exception("Base Program NCA를 찾을 수 없습니다.");

            var extractor = new NcaExtractor(_keySet, ShouldExtract);
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
                    result.ExefsDirs[idOffset] = extractor.ExtractExeFs(effectiveProg, patchProg, exefsName, outDir, progCtx, progress, ct);

                if (effectiveProg.CanOpenSection(NcaSectionType.Data))
                    result.RomfsDirs[idOffset] = extractor.ExtractRomFs(effectiveProg, patchProg, romfsName, outDir, progCtx, ncas.CreateOnlyOffsets.Contains(idOffset), progress, ct);

                var logoDir = extractor.ExtractLogo(effectiveProg, logoName, outDir, progCtx, progress, ct);

                if (logoDir != null) 
                    result.LogoDirs[idOffset] = logoDir;

                ncas.BaseControls.TryGetValue(idOffset, out var baseControl);
                ncas.UpdateControls.TryGetValue(idOffset, out var updateControl);

                var effectiveControl = baseControl ?? updateControl;
                var controlDir = extractor.ExtractControl(effectiveControl, baseControl != null ? updateControl : null, req, result, controlName, outDir, BaseNspPath, progCtx, progress, ct);

                if (controlDir != null) 
                    result.ControlDirs[idOffset] = controlDir;

                ncas.BaseHtmls.TryGetValue(idOffset, out var baseHtml);
                ncas.UpdateHtmls.TryGetValue(idOffset, out var updateHtml);

                var htmlDir = extractor.ExtractHtmlDoc(baseHtml, updateHtml, htmlName, outDir, progCtx, progress, ct);

                if (htmlDir != null) 
                    result.HtmlDocDirs[idOffset] = htmlDir;

                ncas.BaseLegals.TryGetValue(idOffset, out var baseLegal);

                ncas.UpdateLegals.TryGetValue(idOffset, out var updateLegal);
                var legalDir = extractor.ExtractLegal(baseLegal, updateLegal, legalName, outDir, progCtx, progress, ct);

                if (legalDir != null) 
                    result.LegalDirs[idOffset] = legalDir;
            }

            extractor.ExtractDlcs(ncas.DlcNcas, outDir, result, progCtx, progress, ct);

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
            foreach (var s in storages) 
                s.Dispose();

            foreach (var ns in collector.NscStreams) 
                ns.Dispose();
        }
    }

    protected abstract ProgressContext CreateProgressContext(List<string> inputPaths, CollectedNcas ncas);
    protected abstract bool ShouldExtract(string entryPath);

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

            NcaExtractor.TryWithFs(baseProg, updateProg, NcaSectionType.Code, SumFs);
            NcaExtractor.TryWithFs(baseProg, updateProg, NcaSectionType.Data, SumFs);

            if (idOffset == 0 && baseProg.CanOpenSection(NcaSectionType.Logo))
                NcaExtractor.TryWithFs(baseProg, null, NcaSectionType.Logo, SumFs);
        }

        foreach (var (idOffset, baseControl) in ncas.BaseControls)
        {
            ncas.UpdateControls.TryGetValue(idOffset, out var updateControl);
            NcaExtractor.TryWithFs(baseControl, updateControl, NcaSectionType.Data, SumFs);
        }

        foreach (var (idOffset, baseHtml) in ncas.BaseHtmls)
        {
            ncas.UpdateHtmls.TryGetValue(idOffset, out var updateHtml);
            NcaExtractor.TryWithFs(baseHtml, updateHtml, NcaSectionType.Data, SumFs);
        }

        foreach (var (idOffset, baseLegal) in ncas.BaseLegals)
        {
            ncas.UpdateLegals.TryGetValue(idOffset, out var updateLegal);
            NcaExtractor.TryWithFs(baseLegal, updateLegal, NcaSectionType.Data, SumFs);
        }

        foreach (var dlc in ncas.DlcNcas)
            NcaExtractor.TryWithFs(dlc, null, NcaSectionType.Data, SumFs);

        return total;
    }
}