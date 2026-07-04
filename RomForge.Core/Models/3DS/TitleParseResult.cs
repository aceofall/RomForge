using _3DS.Core.Enums;
using _3DS.Core.Models;

namespace RomForge.Core.Models._3DS;

public class TitleParseResult
{
    public InstalledTitle? Title { get; init; }
    public string? FilePath { get; init; }
    public string? ProductCode { get; init; }
    public string? ShortDescription { get; init; }
    public string? Publisher { get; init; }
    public bool Crypto { get; init; }
    public byte[]? IconPixels { get; init; }
    public IReadOnlyList<Locale3dsLanguage> AvailableLanguages { get; init; } = [];
}