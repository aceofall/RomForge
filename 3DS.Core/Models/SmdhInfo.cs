using _3DS.Core.Enums;

namespace _3DS.Core.Models;

public class SmdhInfo
{
    public string ShortDescription { get; init; } = string.Empty;
    public string LongDescription { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string Region {  get; init; } = string.Empty;
    public byte[]? IconPixels { get; init; }
    public int IconWidth { get; init; }
    public int IconHeight { get; init; }
    public IReadOnlyList<Locale3dsLanguage> AvailableLanguages { get; init; } = [];
}