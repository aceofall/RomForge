using CHD.Core.Services;
using Common;
using DolphinTool.Core.Services;
using RomZip.Core.Enums;
using RomZip.Core.Models;
using System.IO;

namespace RomForge.ViewModels.Patch.Services;

public class CompressKnownConverter(Action<string, LogLevel> log, Action<int> setProgress, Action<string> setStatus, int dolphinCompressLevel)
{
    public async Task ConvertAsync(DetectResult detected, string outputPath, string? outputCuePath, List<string> copiedTrackPaths, CancellationToken ct)
    {
        switch (detected.Format)
        {
            case RomFormat.Bin:
                {
                    setStatus("CHD 변환 중...");
                    setProgress(0);

                    FileConverter converter = new();
                    converter.LogMessage += (_, e) => log(e.Message, e.Level);
                    converter.ProgressChanged += (_, e) => setProgress(e.Progress);

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
                    setStatus("CHD 변환 중...");
                    setProgress(0);

                    FileConverter converter = new();
                    converter.LogMessage += (_, e) => log(e.Message, e.Level);
                    converter.ProgressChanged += (_, e) => setProgress(e.Progress);

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
                    setStatus("포맷 변환 중...");
                    setProgress(0);

                    DolphinService dolphin = new();
                    dolphin.LogMessage += (_, e) => log(e.Message, e.Level);
                    dolphin.ProgressChanged += (_, e) => setProgress(e.Progress);

                    await dolphin.ConvertFileAsync(outputPath, detected.Format.ToString(), detected.OutputExtension, dolphinCompressLevel, ct);
                    File.Delete(outputPath);
                    break;
                }
        }
    }
}