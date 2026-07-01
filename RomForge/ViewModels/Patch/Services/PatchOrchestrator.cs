using Common;
using Patch.Core;
using RomZip.Core.Enums;
using RomZip.Core.Models;
using System.IO;

namespace RomForge.ViewModels.Patch.Services;

public class PatchOrchestrator(Action<string, LogLevel> log, Action<int> setProgress, Action<string> setStatus, bool autoCompress, int dolphinCompressLevel)
{
    private string? _outputCuePath;
    private List<string> _copiedTrackPaths = [];
    private readonly BinTrackCopier _binTrackCopier = new (log);
    private readonly ZipCompressor _zipCompressor = new (log, setProgress);
    private readonly CompressKnownConverter _compressKnownConverter = new (log, setProgress, setStatus, dolphinCompressLevel);

    public async Task PatchAsync(string sourcePath, string patchPath, DetectResult detected, string outputDir, string outputPath, bool useBytes, CancellationToken ct)
    {
        _outputCuePath = null;
        _copiedTrackPaths = [];

        bool isZipTarget = detected.Format is not (RomFormat.Bin or RomFormat.Iso or RomFormat.Gcm or RomFormat.Wii or RomFormat.Wbfs);

        if (autoCompress && isZipTarget && useBytes)
        {
            var patched = await UniversalPatcher.ApplyPatchAsync(sourcePath, patchPath, p => setProgress((int)(p * 100)), ct);

            setProgress(100);
            log("패치 완료", LogLevel.Ok);
            setStatus("압축 중...");
            setProgress(0);

            await _zipCompressor.CompressFromBytesAsync(patched, sourcePath, outputDir, ct);

            return;
        }

        await UniversalPatcher.ApplyPatchAsync(sourcePath, patchPath, outputPath, p => setProgress((int)(p * 100)), ct);
        setProgress(100);
        log($"패치 완료: {outputPath}", LogLevel.Ok);

        if (detected.Format == RomFormat.Bin)
            _outputCuePath = await _binTrackCopier.CopyBinTracksAsync(sourcePath, outputDir, outputPath, _copiedTrackPaths);

        if (!autoCompress)
            return;

        if (isZipTarget)
        {
            setStatus("압축 중...");
            setProgress(0);
            await _zipCompressor.CompressFromFileAsync(sourcePath, outputPath, outputDir, ct);
        }
        else
            await _compressKnownConverter.ConvertAsync(detected, outputPath, _outputCuePath, _copiedTrackPaths, ct);
    }

    public void Cleanup(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            if (_outputCuePath is not null && File.Exists(_outputCuePath))
                File.Delete(_outputCuePath);

            foreach (var trackPath in _copiedTrackPaths)
                if (File.Exists(trackPath))
                    File.Delete(trackPath);
        }
        catch (Exception ex)
        {
            log($"파일 정리 실패: {ex.Message}", LogLevel.Error);
        }
    }
}