using LibHac.Common.Keys;
using NSW.M1.Core.Models;

namespace NSW.M1.Core.Services;

public class NspFilteredUnpacker(KeySet keySet, List<string> targetFiles) : NspUnpackerBase(keySet)
{
    private readonly List<string> _targetFiles = targetFiles;

    public UnpackResult Unpack(BuildRequest req, string outDir, bool withControlNca, IProgress<(int pct, string label)>? progress = null, CancellationToken ct = default)
        => UnpackCore(req, outDir, withControlNca, progress, ct);

    protected override ProgressContext CreateProgressContext(List<string> inputPaths, CollectedNcas ncas)
    {
        long matchedSize = CalculateMatchedSize(ncas);
        return new ProgressContext(matchedSize > 0 ? matchedSize : 1);
    }

    protected override bool ShouldExtract(string entryPath) =>
        _targetFiles.Any(t =>
            t.EndsWith(entryPath, StringComparison.OrdinalIgnoreCase) ||
            entryPath.EndsWith(t, StringComparison.OrdinalIgnoreCase));
}