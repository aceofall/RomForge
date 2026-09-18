namespace Vita.Core.Models;

public sealed class ProcessGroup
{
    public required VitaContentCategory Category { get; init; }

    public required string TitleId { get; init; }

    public string? ContentIdSuffix { get; init; }

    public required PatchContext PatchCtx { get; init; }

    public required List<PatchTarget> Targets { get; init; }

    public required List<OwnerContext> Owners { get; init; }
}