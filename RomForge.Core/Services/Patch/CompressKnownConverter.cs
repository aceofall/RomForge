using CHD.Core.Services;
using Common;
using DolphinTool.Core.Services;
using RomForge.Core.Models.Compression;
using System.IO;

namespace RomForge.Core.Services.Patch;

public class CompressKnownConverter(Action<string, LogLevel> log, IProgress<ProgressInfo> progress, int dolphinCompressLevel)
{
    public async Task ConvertAsync(DetectResult detected, string outputPath, string? outputCuePath, List<string> copiedTrackPaths, CancellationToken ct)
    {
        switch (detected.Format)
        {
            case RomFormat.Bin:
                {
                    progress.Report(new ProgressInfo { Label = "CHD 변환 중...", Percent = 0 });

                    FileConverter converter = new(AppConfig.Instance.Chdman.Compression);
                    converter.LogMessage += (_, e) => log(e.Message, e.Level);
                    converter.ProgressChanged += (_, e) => progress.Report(new ProgressInfo { Label = "CHD 변환 중...", Percent = e.Progress });

                    var chdResult = await converter.ConvertFileAsync(outputCuePath!, ct);

                    if (!chdResult.Success)
                        throw new Exception($"CHD 변환 실패: {chdResult.Message}");

                    File.Delete(outputPath);
                    File.Delete(outputCuePath!);

                    foreach (var trackPath in copiedTrackPaths)
                        if (File.Exists(trackPath))
                            File.Delete(trackPath);

                    copiedTrackPaths.Clear();
                    break;
                }
            case RomFormat.Iso:
                {
                    progress.Report(new ProgressInfo { Label = "CHD 변환 중...", Percent = 0 });

                    FileConverter converter = new(AppConfig.Instance.Chdman.Compression);
                    converter.LogMessage += (_, e) => log(e.Message, e.Level);
                    converter.ProgressChanged += (_, e) => progress.Report(new ProgressInfo { Label = "CHD 변환 중...", Percent = e.Progress });

                    var chdResult = await converter.ConvertFileAsync(outputPath, ct);

                    if (!chdResult.Success)
                        throw new Exception($"CHD 변환 실패: {chdResult.Message}");

                    File.Delete(outputPath);
                    break;
                }
            case RomFormat.Gcm:
            case RomFormat.Wii:
            case RomFormat.Wbfs:
                {
                    progress.Report(new ProgressInfo { Label = "포맷 변환 중...", Percent = 0 });

                    DolphinService dolphin = new();
                    dolphin.LogMessage += (_, e) => log(e.Message, e.Level);
                    dolphin.ProgressChanged += (_, e) => progress.Report(new ProgressInfo { Label = "포맷 변환 중...", Percent = e.Progress });

                    await dolphin.ConvertFileAsync(outputPath, detected.Format.ToString(), detected.OutputExtension, dolphinCompressLevel, ct);
                    File.Delete(outputPath);
                    break;
                }
        }
    }
}