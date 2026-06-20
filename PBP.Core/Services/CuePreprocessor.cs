using PBP.Core.Models;
namespace PBP.Core.Services;

public static class CuePreprocessor
{
    public static ResolvedDisc Resolve(string inputPath, string? tempDir = null)
    {
        if (!Path.GetExtension(inputPath).Equals(".cue", StringComparison.InvariantCultureIgnoreCase))
        {
            var size = (uint)new FileInfo(inputPath).Length;

            return ResolvedDisc.Create(new FileStream(inputPath, FileMode.Open, FileAccess.Read), size, TocBuilder.BuildSingleTrackToc(size));
        }

        var cueFile = CueFileReader.Read(inputPath);

        if (cueFile.FileEntries.Count > 1)
        {
            tempDir ??= Path.GetTempPath();

            var tempBinPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.bin");

            CueFile merged;
            using (var outputStream = new FileStream(tempBinPath, FileMode.Create, FileAccess.Write))
                merged = CueMerger.MergeBins(outputStream, cueFile);

            var size = (uint)new FileInfo(tempBinPath).Length;

            return ResolvedDisc.Create(new FileStream(tempBinPath, FileMode.Open, FileAccess.Read), size, TocBuilder.BuildToc(merged, size), tempBinPath);
        }

        var binPath = CueFileResolver.GetBinPath(inputPath);
        var binSize = (uint)new FileInfo(binPath).Length;

        return ResolvedDisc.Create(new FileStream(binPath, FileMode.Open, FileAccess.Read), binSize, TocBuilder.BuildToc(cueFile, binSize));
    }
}