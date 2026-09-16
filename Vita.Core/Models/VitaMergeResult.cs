namespace Vita.Core.Models;

public sealed class VitaMergeResult
{
    public required int TotalFiles { get; init; }

    public required int PatchCandidates { get; init; }

    public required int PatchedSuccessfully { get; init; }
}