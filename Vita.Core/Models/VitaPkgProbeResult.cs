namespace Vita.Core.Models;

public sealed class VitaPkgProbeResult
{
    public required string TitleId { get; init; }

    public required VitaContentCategory Category { get; init; }

    public string? ContentIdSuffix { get; init; }

    public required string ContentId { get; init; }
}