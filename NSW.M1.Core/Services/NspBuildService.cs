using Common;
using LibHac.Common.Keys;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using NSW.Core;
using NSW.Core.Enums;
using NSW.HacPack.Enums;
using NSW.HacPack.Services;
using NSW.M1.Core.Models;
using NSW.Utils;
using System.Diagnostics;
using Path = System.IO.Path;

namespace NSW.M1.Core.Services;

public static class NspBuildService
{
    public static string Run(BuildRequest req, BuildMode mode, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct = default)
    {
        var keySet = KeySetProvider.Instance.KeySet;
        return RunProcess(req, mode, keySet, progress, log, ct);
    }

    private static string RunProcess(BuildRequest req, BuildMode mode, KeySet keySet, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct = default)
    {
        var dirs = new WorkDirs(req.OutputDir);

        if (mode != BuildMode.RebuildOnly)
            dirs.Prepare();
        else
        {
            if (Directory.Exists(dirs.BuildNca))
                Directory.Delete(dirs.BuildNca, true);
            Directory.CreateDirectory(dirs.BuildNca);
        }

        UnpackResult unpackResult;

        if (mode == BuildMode.RebuildOnly)
        {
            log("━━ 기존 언팩 데이터 스캔 중... ━━", LogLevel.Info);
            unpackResult = ScanExistingUnpackedDir(dirs.Unpacked, req.OverrideSdkVersion, req.OverrideKeyGeneration);
        }
        else
        {
            unpackResult = StepUnpack(req, keySet, dirs, progress, log, ct);

            if (mode == BuildMode.UnpackOnly)
            {
                log("━━ 언팩 완료 ━━", LogLevel.Ok);
                return dirs.Unpacked;
            }
        }

        var settingsList = StepBuildSettings(req, unpackResult, keySet, dirs, log);

        if (req.TargetIdOffset.HasValue)
        {
            settingsList = [.. settingsList.Where(s => s.IdOffset == req.TargetIdOffset.Value)];

            if (settingsList.Count == 0)
                throw new Exception($"IdOffset {req.TargetIdOffset.Value}에 해당하는 타이틀이 없습니다.");

            var s = settingsList[0];
            s.TitleId += req.TargetIdOffset.Value;
            s.IdOffset = 0;
        }

        foreach (var settings in settingsList)
        {
            settings.TitleVersion = unpackResult.GameVersion;
            settings.Language = req.Language;
            settings.UserMetadata = req.UserMetadata;
            settings.KeySet = keySet;
        }

        var baseSettings = settingsList.First(s => s.IdOffset == 0);

        foreach (var settings in settingsList)
        {
            StepNpdm(settings, log, ct);
            StepProgramNca(req, settings, unpackResult, progress, log, ct);
        }

        StepManualNcas(settingsList, unpackResult, progress, log, ct);

        foreach (var settings in settingsList)
            StepControlNca(settings, unpackResult, progress, log, ct);

        StepBuildDlcNsps(req, dirs, baseSettings, progress, log, ct);
        StepMetaNca(settingsList, progress, log, ct);

        return StepPackage(req, baseSettings, unpackResult, progress, log, ct);
    }

    private static UnpackResult StepUnpack(BuildRequest req, KeySet libHacKeySet, WorkDirs dirs, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        log("━━ 1단계(1/9): 언패킹 ━━", LogLevel.Info);
        progress.Report((0, "언패킹 중..."));

        var unpacker = new NspUnpacker(libHacKeySet);
        var result = unpacker.Unpack(req, dirs.Unpacked, progress, ct);

        log($"  언패킹 완료 ({sw.Elapsed.TotalSeconds:F2}s)", LogLevel.Ok);
        return result;
    }

    private static List<NcaGenerationOptions> StepBuildSettings(BuildRequest req, UnpackResult result, KeySet keySet, WorkDirs dirs, Action<string, LogLevel> log)
    {
        var sw = Stopwatch.StartNew();
        log("━━ 2단계(2/9): 설정 초기화 ━━", LogLevel.Info);

        byte keyGeneration = result.BaseKeyGeneration == 0 ? (byte)1 : result.BaseKeyGeneration;
        int keygenIdx = keyGeneration - 1;

        if (keygenIdx < 0)
            throw new Exception($"유효하지 않은 KeyGeneration입니다: {keyGeneration}");

        var settingsList = new List<NcaGenerationOptions>();

        foreach (var (idOffset, exefsDir) in result.ExefsDirs.OrderBy(kv => kv.Key))
        {
            result.RomfsDirs.TryGetValue(idOffset, out var romfsDir);
            result.LogoDirs.TryGetValue(idOffset, out var logoDir);

            var settings = new NcaGenerationOptions
            {
                IdOffset = idOffset,
                TitleId = result.TitleId,
                TempDirectory = dirs.Temp,
                OutDirectory = dirs.BuildNca,
                ExefsDirectory = exefsDir,
                RomfsDirectory = romfsDir ?? string.Empty,
                LogoDirectory = logoDir ?? string.Empty,
                BackupDirectory = Path.Combine(Path.GetDirectoryName(dirs.Temp)!, "backups"),
                Plaintext = 0,
                HasTitleKey = 0,
                NcaSig = NcaSigType.Zero,
                TitleType = LibHac.Ncm.ContentMetaType.Application,
                SdkVersion = req.OverrideSdkVersion ?? result.BaseSdkVersion,
                KeyGeneration = req.OverrideKeyGeneration ?? keyGeneration,
                KeySet = keySet
            };

            if (keygenIdx >= settings.KeySet.KeyAreaKeys.Length)
                throw new Exception($"유효하지 않은 KeyGeneration입니다: {keyGeneration}");

            settings.KeyAreaKey = settings.KeySet.KeyAreaKeys[keygenIdx][0].DataRo.ToArray();
            settingsList.Add(settings);
        }

        var first = settingsList[0];
        log($"  TitleId: {first.TitleId:x16}  KeyGen: {first.KeyGeneration}  SDK: {first.SdkVersionString}", LogLevel.Ok);
        log($"  설정 완료 ({sw.Elapsed.TotalSeconds:F2}s)", LogLevel.Ok);
        return settingsList;
    }

    private static void StepNpdm(NcaGenerationOptions settings, Action<string, LogLevel> log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        log($"━━ 3단계(3/9): NPDM 처리 (IdOffset={settings.IdOffset}) ━━", LogLevel.Info);

        NpdmProcessor.PatchNpdmMetadata(settings);
        log($"  NPDM 완료 ({sw.Elapsed.TotalSeconds:F2}s)", LogLevel.Ok);
    }

    private static void StepProgramNca(BuildRequest req, NcaGenerationOptions settings, UnpackResult unpackResult, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        log("━━ 4단계(4/9): Program NCA 생성 ━━", LogLevel.Info);
        progress.Report((0, "Program NCA 생성 중..."));

        if (req.HasPatch)
            ApplyPatch(req.PatchDir, unpackResult, progress, log);

        settings.NcaType = LibHac.FsSystem.NcaHeader.ContentType.Program;
        settings.ProgramNcaPath = NcaGenerator.GenerateProgramNca(settings, progress, ct) ?? string.Empty;
        log($"  Program NCA: {Path.GetFileName(settings.ProgramNcaPath)} ({sw.Elapsed.TotalSeconds}s)", LogLevel.Ok);
    }

    private static void ApplyPatch(string patchDir, UnpackResult unpackResult, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log)
    {
        string exefsDir = unpackResult.ExefsDirs.GetValueOrDefault((byte)0, string.Empty);
        string romfsDir = unpackResult.RomfsDirs.GetValueOrDefault((byte)0, string.Empty);

        string patchExefs = Path.Combine(patchDir, "exefs");
        string patchRomfs = Path.Combine(patchDir, "romfs");

        if (Directory.Exists(patchExefs))
        {
            progress.Report((-1, "한글패치 ExeFS 병합 중..."));
            log($"  한글패치 ExeFS 병합: {patchExefs}", LogLevel.Info);
            MergeDirectory(patchExefs, exefsDir);
        }
        if (Directory.Exists(patchRomfs))
        {
            progress.Report((-1, "한글패치 RomFS 병합 중..."));
            log($"  한글패치 RomFS 병합: {patchRomfs}", LogLevel.Info);
            MergeDirectory(patchRomfs, romfsDir);
        }

        if (Directory.Exists(patchDir))
        {
            var xdeltaFiles = Directory.EnumerateFiles(patchDir, "*.xdelta", SearchOption.AllDirectories)
                                       .OrderBy(f => f)
                                       .ToList();

            if (xdeltaFiles.Count > 0)
            {
                progress.Report((-1, "xdelta 바이너리 패치 적용 중..."));
                log($"  발견된 xdelta 패치 수: {xdeltaFiles.Count}개", LogLevel.Info);

                string unpackedRoot = Path.GetDirectoryName(exefsDir)!;

                foreach (var xdeltaPath in xdeltaFiles)
                {
                    string targetFileName = Path.GetFileNameWithoutExtension(xdeltaPath);
                    string relativePath = Path.GetRelativePath(patchDir, xdeltaPath);
                    string relativeTargetKey = Path.Combine(Path.GetDirectoryName(relativePath) ?? string.Empty, targetFileName);
                    var targetFiles = new List<string>();

                    string absoluteExactPath = Path.Combine(unpackedRoot, relativeTargetKey);
                    if (File.Exists(absoluteExactPath))
                    {
                        targetFiles.Add(absoluteExactPath);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(exefsDir))
                            targetFiles.AddRange(Directory.EnumerateFiles(exefsDir, targetFileName, SearchOption.AllDirectories));
                        if (!string.IsNullOrEmpty(romfsDir))
                            targetFiles.AddRange(Directory.EnumerateFiles(romfsDir, targetFileName, SearchOption.AllDirectories));
                    }

                    if (targetFiles.Count > 0)
                    {
                        foreach (var targetPath in targetFiles.Distinct())
                        {
                            string displayPath = Path.GetRelativePath(unpackedRoot, targetPath);
                            log($"  xdelta 패치 적용: {Path.GetFileName(xdeltaPath)} ➡️ {displayPath}", LogLevel.Info);

                            string tempOutPath = targetPath + ".patched";

                            try
                            {
                                Patch.Core.Formats.Xdelta3.ApplyPatch(
                                    targetPath,
                                    Path.GetFullPath(xdeltaPath),
                                    tempOutPath,
                                    percent =>
                                    {
                                        int currentStep = 80 + (int)(percent * 10);
                                        if (currentStep > 80)
                                            progress?.Report((currentStep, string.Empty));
                                    }
                                );

                                if (File.Exists(tempOutPath))
                                {
                                    File.Delete(targetPath);
                                    File.Move(tempOutPath, targetPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                log($"  ❌ xdelta 패치 실패 ({Path.GetFileName(xdeltaPath)}): {ex.Message}", LogLevel.Error);
                                if (File.Exists(tempOutPath)) File.Delete(tempOutPath);
                            }
                        }
                    }
                    else
                    {
                        log($"  ⚠️ xdelta 대상 원본 파일을 찾을 수 없음: {targetFileName}", LogLevel.Info);
                    }
                }
            }
        }
    }

    private static void StepManualNcas(List<NcaGenerationOptions> settingsList, UnpackResult unpackResult, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct)
    {
        log("━━ 5단계(5/9): Manual NCA 생성 ━━", LogLevel.Info);

        foreach (var (idOffset, htmlDir) in unpackResult.HtmlDocDirs)
        {
            if (!Directory.Exists(htmlDir) || Directory.GetFileSystemEntries(htmlDir).Length == 0) continue;
            var settings = settingsList.FirstOrDefault(s => s.IdOffset == idOffset) ?? settingsList[0];
            string type = idOffset == 0 ? "htmldoc" : $"htmldoc{idOffset}";
            log($"  [{type}] 매뉴얼 빌드 시작...", LogLevel.Info);
            var manualSettings = settings.WithRomfs(htmlDir, LibHac.FsSystem.NcaHeader.ContentType.Manual);
            var currentNca = NcaGenerator.GenerateRomfsNca(manualSettings, "Manual", progress, ct);
            if (currentNca == null) continue;
            settings.ManualNcaPaths.Add(currentNca);
            settings.HtmlDocNcaPath = currentNca;
            log($"  [{type}] NCA 등록: {Path.GetFileName(currentNca)}", LogLevel.Ok);
        }

        foreach (var (idOffset, legalDir) in unpackResult.LegalDirs)
        {
            if (!Directory.Exists(legalDir) || Directory.GetFileSystemEntries(legalDir).Length == 0) continue;
            var settings = settingsList.FirstOrDefault(s => s.IdOffset == idOffset) ?? settingsList[0];
            string type = idOffset == 0 ? "legal" : $"legal{idOffset}";
            log($"  [{type}] 매뉴얼 빌드 시작...", LogLevel.Info);
            var manualSettings = settings.WithRomfs(legalDir, LibHac.FsSystem.NcaHeader.ContentType.Manual);
            var currentNca = NcaGenerator.GenerateRomfsNca(manualSettings, "Manual", progress, ct);
            if (currentNca == null) continue;
            settings.ManualNcaPaths.Add(currentNca);
            settings.LegalNcaPath = currentNca;
            log($"  [{type}] NCA 등록: {Path.GetFileName(currentNca)}", LogLevel.Ok);
        }
    }

    private static void StepControlNca(NcaGenerationOptions settings, UnpackResult unpackResult, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        log($"━━ 6단계(6/9): Control NCA 생성 (IdOffset={settings.IdOffset}) ━━", LogLevel.Info);
        progress.Report((0, "Control NCA 생성 중..."));

        if (!unpackResult.ControlDirs.TryGetValue(settings.IdOffset, out var controlDir))
            return;

        var controlSettings = settings.WithRomfs(controlDir, LibHac.FsSystem.NcaHeader.ContentType.Control);
        NacpProcessor.ProcessControlMetadata(controlSettings);
        settings.ControlNcaPath = NcaGenerator.GenerateRomfsNca(controlSettings, "Control", progress, ct) ?? string.Empty;

        log($"  Control NCA: {Path.GetFileName(settings.ControlNcaPath)} ({sw.Elapsed.TotalSeconds:F2}s)", LogLevel.Ok);
    }

    private static void StepBuildDlcNsps(BuildRequest req, WorkDirs dirs, NcaGenerationOptions baseSettings, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct)
    {
        string dlcBaseDir = Path.Combine(dirs.Unpacked, "DLCs");
        if (!Directory.Exists(dlcBaseDir)) return;
        var sw = Stopwatch.StartNew();
        log("━━ 7단계(7/9): DLC 빌드 시작 ━━", LogLevel.Info);

        int dlcCount = 0;

        foreach (var dlcDir in Directory.GetDirectories(dlcBaseDir))
        {
            ct.ThrowIfCancellationRequested();

            string titleIdStr = Path.GetFileName(dlcDir);
            if (!ulong.TryParse(titleIdStr, System.Globalization.NumberStyles.HexNumber, null, out ulong titleId))
                continue;

            string romfsPath = Path.Combine(dlcDir, "romfs");
            bool hasRomfs = Directory.Exists(romfsPath) && Directory.EnumerateFileSystemEntries(romfsPath).Any();

            log($"  DLC 빌드 시도: {titleIdStr}", LogLevel.Info);

            var dlcSettings = new NcaGenerationOptions
            {
                TitleId = titleId,
                TempDirectory = Path.Combine(dirs.Temp, "dlc_" + titleIdStr),
                OutDirectory = baseSettings.OutDirectory,
                RomfsDirectory = hasRomfs ? romfsPath : string.Empty,
                TitleType = LibHac.Ncm.ContentMetaType.AddOnContent,
                NcaType = LibHac.FsSystem.NcaHeader.ContentType.PublicData,
                SdkVersion = req.OverrideSdkVersion ?? baseSettings.SdkVersion,
                KeyGeneration = req.OverrideKeyGeneration ?? baseSettings.KeyGeneration,
                KeySet = baseSettings.KeySet,
                KeyAreaKey = baseSettings.KeyAreaKey,
                NcaSig = NcaSigType.Zero,
                Plaintext = 0
            };

            try
            {
                Directory.CreateDirectory(dlcSettings.TempDirectory);

                if (hasRomfs)
                    dlcSettings.PublicDataNcaPath = NcaGenerator.GenerateRomfsNca(dlcSettings, "DLC", progress, ct) ?? string.Empty;

                NcaGenerator.GenerateMetaNca([dlcSettings], progress, ct);

                log($"  DLC 완료: {titleIdStr}", LogLevel.Ok);
                dlcCount++;
            }
            catch (Exception ex)
            {
                log($"  DLC 실패: {titleIdStr} - {ex.Message}", LogLevel.Error);
            }
            finally
            {
                if (Directory.Exists(dlcSettings.TempDirectory))
                    Directory.Delete(dlcSettings.TempDirectory, true);
            }
        }

        log($"  총 ({dlcCount})개의 DLC 빌드 완료 : ({sw.Elapsed.TotalSeconds:F2}s)", LogLevel.Ok);
    }

    private static void StepMetaNca(List<NcaGenerationOptions> settingsList, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        log("━━ 8단계(8/9): Meta NCA 생성 ━━", LogLevel.Info);
        progress.Report((-1, "Meta NCA 생성 중..."));

        var baseSettings = settingsList.First(s => s.IdOffset == 0);

        NcaGenerator.GenerateMetaNca(settingsList, progress, ct);

        var metaFiles = Directory.GetFiles(baseSettings.OutDirectory, "*.cnmt.nca")
                                 .OrderByDescending(File.GetLastWriteTime);

        foreach (var file in metaFiles)
        {
            try
            {
                using var fs = File.OpenRead(file);
                var nca = new Nca(baseSettings.KeySet, fs.AsStorage());

                if (nca.Header.ContentType == NcaContentType.Meta)
                {
                    baseSettings.MetaNcaPath = file;
                    break;
                }
            }
            catch
            {
                continue;
            }
        }

        if (string.IsNullOrEmpty(baseSettings.MetaNcaPath))
            log("  Meta NCA를 찾을 수 없습니다!", LogLevel.Error);
        else
            log($"  Meta NCA: {Path.GetFileName(baseSettings.MetaNcaPath)} ({sw.Elapsed.TotalSeconds:F2}s)", LogLevel.Ok);
    }

    private static string StepPackage(BuildRequest req, NcaGenerationOptions settings, UnpackResult unpackResult, IProgress<(int pct, string label)> progress, Action<string, LogLevel> log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        log("━━ 9단계(9/9): NSP 패키징 ━━", LogLevel.Info);
        progress.Report((0, "NSP 패키징 중..."));

        Directory.CreateDirectory(req.OutputDir);

        string suffix = req.HasPatch ? "Patched_Repack.nsp" : "Repack.nsp";

        string displayVersion = string.IsNullOrWhiteSpace(unpackResult.DisplayVersion) ? "1.0.0" : unpackResult.DisplayVersion;
        string fileName = NspNameBuilder.FileNameBuild(suffix, unpackResult.KrTitle, unpackResult.EnTitle, unpackResult.TitleIdStr.ToUpper(), displayVersion, unpackResult.GameVersion, unpackResult.DlcCount);
        string finalNsp = Path.Combine(req.OutputDir, fileName);
        finalNsp = Common.Utils.GetUniqueFilePath(finalNsp);

        var fileStreams = new List<(string, Stream)>();

        try
        {
            foreach (var f in Directory.GetFiles(settings.OutDirectory).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
                fileStreams.Add((Path.GetFileName(f), File.OpenRead(f)));

            using var outStream = File.Open(finalNsp, FileMode.Create, FileAccess.Write);
            Pfs0Builder.BuildFromMemoryStreams(fileStreams, outStream, progress, ct);
        }
        finally
        {
            foreach (var (_, stream) in fileStreams)
                stream.Dispose();

            var dirs = new WorkDirs(req.OutputDir);

            if (Directory.Exists(dirs.BuildNca))
                Directory.Delete(dirs.BuildNca, true);
            if (Directory.Exists(dirs.Temp))
                Directory.Delete(dirs.Temp, true);
        }

        progress.Report((100, "완료"));
        log($"  출력: {finalNsp} ({sw.Elapsed.TotalSeconds:F2}s)", LogLevel.Ok);
        return finalNsp;
    }

    private static UnpackResult ScanExistingUnpackedDir(string unpackedDir, uint? overrideSdkVersion = null, byte? overrideKeyGeneration = null)
    {
        string nacpPath = Path.Combine(unpackedDir, "control", "control.nacp");
        string controlFile = Directory.GetFiles(unpackedDir, "control*.nca").FirstOrDefault() ?? string.Empty;

        var (krTitle, enTitle, displayVersion, titleId) = LibHacHelper.ReadNacpInfo(nacpPath);

        byte keyGeneration = 1;
        uint sdkVersion = 0;
        uint gameVersion = 0;

        if (File.Exists(controlFile))
        {
            (keyGeneration, sdkVersion) = LibHacHelper.ReadControlNcaInfo(controlFile);

            string fileName = Path.GetFileNameWithoutExtension(controlFile);

            if (fileName.Contains('_'))
                _ = uint.TryParse(fileName.Split('_')[1], out gameVersion);
        }

        var dlcs = new List<DlcUnpackInfo>();
        string dlcBaseDir = Path.Combine(unpackedDir, "DLCs");

        if (Directory.Exists(dlcBaseDir))
        {
            foreach (var dlcDir in Directory.GetDirectories(dlcBaseDir))
            {
                string titleIdStr = Path.GetFileName(dlcDir);
                if (ulong.TryParse(titleIdStr, System.Globalization.NumberStyles.HexNumber, null, out ulong dlcTitleId))
                {
                    dlcs.Add(new DlcUnpackInfo
                    {
                        TitleId = dlcTitleId,
                        Dir = Path.Combine("DLCs", titleIdStr)
                    });
                }
            }
        }

        var exefsDirs = new Dictionary<byte, string>();
        var romfsDirs = new Dictionary<byte, string>();
        var logoDirs = new Dictionary<byte, string>();
        var controlDirs = new Dictionary<byte, string>();
        var htmlDocDirs = new Dictionary<byte, string>();
        var legalDirs = new Dictionary<byte, string>();

        for (byte i = 0; i < 16; i++)
        {
            string suffix = i == 0 ? string.Empty : i.ToString();

            string exefs = Path.Combine(unpackedDir, $"exefs{suffix}");
            if (Directory.Exists(exefs)) exefsDirs[i] = exefs;

            string romfs = Path.Combine(unpackedDir, $"romfs{suffix}");
            if (Directory.Exists(romfs)) romfsDirs[i] = romfs;

            string logo = Path.Combine(unpackedDir, $"logo{suffix}");
            if (Directory.Exists(logo)) logoDirs[i] = logo;

            string control = Path.Combine(unpackedDir, $"control{suffix}");
            if (Directory.Exists(control)) controlDirs[i] = control;

            string htmldoc = Path.Combine(unpackedDir, $"htmldoc{suffix}");
            if (Directory.Exists(htmldoc)) htmlDocDirs[i] = htmldoc;

            string legal = Path.Combine(unpackedDir, $"legal{suffix}");
            if (Directory.Exists(legal)) legalDirs[i] = legal;

            if (i > 0 && !exefsDirs.ContainsKey(i) && !romfsDirs.ContainsKey(i)) 
                break;
        }

        return new UnpackResult
        {
            TitleId = titleId,
            GameVersion = gameVersion,
            BaseSdkVersion = overrideSdkVersion ?? sdkVersion,
            BaseKeyGeneration = overrideKeyGeneration ?? keyGeneration,
            DisplayVersion = displayVersion,
            KrTitle = krTitle,
            EnTitle = enTitle,
            ExefsDirs = exefsDirs,
            RomfsDirs = romfsDirs,
            LogoDirs = logoDirs,
            ControlDirs = controlDirs,
            HtmlDocDirs = htmlDocDirs,
            LegalDirs = legalDirs,            
            Dlcs = dlcs
        };
    }

    private static void MergeDirectory(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(srcDir, file);
            string dest = Path.Combine(dstDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}