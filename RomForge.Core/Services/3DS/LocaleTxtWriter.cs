using _3DS.Core.Enums;
using System.IO;

namespace RomForge.Core.Services._3DS;

public static class LocaleTxtWriter
{
    public static readonly Dictionary<Locale3dsLanguage, LocaleRegion> LanguageToRegion = new()
    {
        [Locale3dsLanguage.JP] = LocaleRegion.JPN,
        [Locale3dsLanguage.EN] = LocaleRegion.USA,
        [Locale3dsLanguage.FR] = LocaleRegion.EUR,
        [Locale3dsLanguage.DE] = LocaleRegion.EUR,
        [Locale3dsLanguage.IT] = LocaleRegion.EUR,
        [Locale3dsLanguage.ES] = LocaleRegion.EUR,
        [Locale3dsLanguage.ZH] = LocaleRegion.CHN,
        [Locale3dsLanguage.KO] = LocaleRegion.KOR,
        [Locale3dsLanguage.NL] = LocaleRegion.EUR,
        [Locale3dsLanguage.PT] = LocaleRegion.EUR,
        [Locale3dsLanguage.RU] = LocaleRegion.EUR,
        [Locale3dsLanguage.TW] = LocaleRegion.TWN,
    };

    public static async Task WriteAsync(string sdRoot, string titleId, Locale3dsLanguage language, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sdRoot))
            throw new ArgumentException("SD 루트 경로가 비어있습니다.", nameof(sdRoot));

        if (string.IsNullOrEmpty(titleId) || titleId.Length != 16)
            throw new ArgumentException("타이틀 ID가 올바르지 않습니다.", nameof(titleId));

        if (language == Locale3dsLanguage.None)
            return;

        if (!LanguageToRegion.TryGetValue(language, out LocaleRegion region))
            return;

        string dir = Path.Combine(sdRoot, "luma", "titles", titleId.ToUpperInvariant());

        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "locale.txt");
        string content = $"{region} {language}";

        await File.WriteAllTextAsync(path, content, ct);
    }
}