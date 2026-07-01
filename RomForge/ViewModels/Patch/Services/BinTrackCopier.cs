using CHD.Core.Services;
using Common;
using System.IO;

namespace RomForge.ViewModels.Patch.Services;

public class BinTrackCopier(Action<string, LogLevel> log)
{
    public async Task<string?> CopyBinTracksAsync(string sourcePath, string outputDir, string outputPath, List<string> copiedTrackPaths)
    {
        string? cuePath = Directory.GetFiles(Path.GetDirectoryName(sourcePath)!, "*.cue")
            .FirstOrDefault(c => ConversionSource.ParseBinsFromCue(c)
                .Any(b => string.Equals(Path.GetFileName(b), Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase)));

        if (cuePath is null)
        {
            log("CUE 파일을 찾을 수 없습니다.", LogLevel.Error);
            return null;
        }

        var sourceDir = Path.GetDirectoryName(cuePath)!;
        var referencedBins = ConversionSource.ParseBinsFromCue(cuePath);

        foreach (var binName in referencedBins)
        {
            string sourceBinPath = Path.Combine(sourceDir, Path.GetFileName(binName));
            string targetBinPath = Path.Combine(outputDir, Path.GetFileName(binName));

            if (!string.Equals(targetBinPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(sourceBinPath))
                {
                    File.Copy(sourceBinPath, targetBinPath, true);
                    copiedTrackPaths.Add(targetBinPath);
                }
                else
                {
                    log($"멀티 트랙 파일을 찾을 수 없습니다: {Path.GetFileName(sourceBinPath)}", LogLevel.Error);
                    return null;
                }
            }
        }

        string outputCuePath = Path.Combine(outputDir, Path.GetFileName(cuePath));
        File.Copy(cuePath, outputCuePath, true);

        return outputCuePath;
    }
}