namespace Vita.Core.Models;

public sealed class PatchTarget
{
    public required string RelativePath { get; init; }

    public required PatchTargetKind Kind { get; init; }

    public PatchAppEntry? Source { get; init; }

    public required string PatchFileRel { get; init; }

    public required long EstimatedSize { get; init; }
}