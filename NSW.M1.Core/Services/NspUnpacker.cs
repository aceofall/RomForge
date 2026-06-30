using LibHac.Common.Keys;
using NSW.M1.Core.Models;

namespace NSW.M1.Core.Services;

public class NspUnpacker(KeySet keySet) : NspUnpackerBase(keySet)
{
    public UnpackResult Unpack(BuildRequest req, string outDir, IProgress<(int pct, string label)>? progress = null, CancellationToken ct = default)
        => UnpackCore(req, outDir, true, progress, ct);

    protected override ProgressContext CreateProgressContext(List<string> inputPaths, CollectedNcas ncas)
    {
        long totalInputSize = inputPaths.Sum(p => new FileInfo(p).Length);
        return new ProgressContext(totalInputSize);
    }

    protected override bool ShouldExtract(string entryPath) => true;
}