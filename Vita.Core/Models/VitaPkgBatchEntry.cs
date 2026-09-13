namespace Vita.Core.Models;

public sealed class VitaPkgBatchEntry
{
    public required string PkgPath { get; init; }

    public required string License { get; init; }

    public string? PatchPath { get; init; }

    public required VitaPkgProbeResult Probe { get; init; }
}