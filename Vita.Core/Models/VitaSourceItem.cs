using Vita.Core.Services;

namespace Vita.Core.Models;

public sealed class VitaSourceItem
{
    public required VitaContentCategory Category { get; init; }

    public required string TitleId { get; init; }

    public string? ContentIdSuffix { get; init; }

    public required string SourcePath { get; init; }

    public IVitaSourceAccessor? Accessor { get; init; }

    public string? PatchPathOverride { get; init; }
}