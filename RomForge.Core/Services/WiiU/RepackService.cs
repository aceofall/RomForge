using Common;
using System.Diagnostics;
using System.IO;
using WiiU.Core.Services;

namespace RomForge.Core.Services.WiiU;

public sealed class RepackService(Action<string, LogLevel> log, Func<string> getPatchPath)
{
    public async Task UnpackAsync(string inputPath, string outputPath, string keysTxtPath, Action<ProgressInfo>? progress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var source = UnpackService.Open(inputPath, keysTxtPath);

            log($"타이틀 ID: {source.TitleIdHex}, 버전: {source.TitleVersion}", LogLevel.Info);

            Directory.CreateDirectory(outputPath);

            var sw = Stopwatch.StartNew();

            source.ExtractTo(outputPath,
                onFileProgress: (done, total, path) =>
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Invoke(new ProgressInfo
                    {
                        Percent = total > 0 ? (int)(done * 100.0 / total) : 100,
                        Label = path,
                        TimeInfo = $"{sw.Elapsed:mm\\:ss} 경과",
                        Speed = string.Empty,
                    });
                },
                cancellationToken: ct);
        }, ct);
    }

    public async Task RepackAsync(string unpackedPath, string outputFolder, Action<ProgressInfo>? progress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var source = new FolderTitleSource(unpackedPath);
            RunRepack(source, outputFolder, progress, ct);
        }, ct);
    }

    public async Task RepackDirectAsync(string inputPath, string keysTxtPath, string outputFolder, Action<ProgressInfo>? progress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var source = UnpackService.Open(inputPath, keysTxtPath);
            RunRepack(source, outputFolder, progress, ct);
        }, ct);
    }

    private void RunRepack(ITitleSource source, string outputFolder, Action<ProgressInfo>? progress, CancellationToken ct)
    {
        log($"타이틀 ID: {source.TitleIdHex}, 버전: {source.TitleVersion}", LogLevel.Info);

        string patchPath = getPatchPath();
        string? patchFolder = string.IsNullOrEmpty(patchPath) ? null : patchPath;
        if (patchFolder is not null)
            log($"한글패치 적용: {patchFolder}", LogLevel.Info);

        Directory.CreateDirectory(outputFolder);
        string outputWuaPath = Utils.GetUniqueFilePath(
            Path.Combine(outputFolder, $"{source.TitleIdHex}_v{source.TitleVersion}_Repack.wua"));

        var sw = Stopwatch.StartNew();

        global::WiiU.Core.Services.RepackService.Repack(
            source,
            outputWuaPath,
            patchFolder,
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
            cancellationToken: ct);

        log($"완료: {outputWuaPath}", LogLevel.Ok);
    }
}