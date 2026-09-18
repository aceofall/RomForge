using Vita.Core.Services;

namespace Vita.Core.Models;

public sealed class PatchContext
{
    public required IVitaSourceAccessor Accessor { get; init; }

    public required Dictionary<string, string> PatchFiles { get; init; }

    public required List<string> RawOverwriteFiles { get; init; }
}