namespace Vita.Core.Models;

public sealed class VitaBatchSourceEntry
{
    public required VitaSourceKind Kind { get; init; }

    public required string Path { get; init; }

    public string? License { get; init; }

    public string? PatchPath { get; init; }

    public VitaPkgProbeResult? Probe { get; init; }

    public VitaContentCategory? ItemCategory { get; init; }

    public string? ItemTitleId { get; init; }

    public string? ItemContentIdSuffix { get; init; }

    public string? ItemSourcePath { get; init; }
}