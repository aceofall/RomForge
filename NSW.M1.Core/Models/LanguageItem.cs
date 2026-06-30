using static LibHac.Ns.ApplicationControlProperty;

namespace NSW.M1.Core.Models;

public class LanguageItem
{
    public Language Language { get; set; }

    public string LanguageCode => Language.ToString();

    public string TitleName { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public Byte[] Logo { get; set; }

    public bool IsSelected { get; set; }
}