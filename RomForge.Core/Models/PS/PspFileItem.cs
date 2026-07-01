using Common.WPF.ViewModels;
using System.Windows.Media;

namespace RomForge.Core.Models.PS;

public class PspFileItem(string filePath) : ConvertibleFileItemBase(filePath, "미지원")
{
    public Brush ExtensionBackground => ExtensionColorMap.Resolve(Extension, ColorMap);

    private static readonly Dictionary<string, string> ColorMap = new()
    {
        ["iso"] = "#94C8FF",
        ["cso"] = "#94FFB5",
        ["chd"] = "#D494FF",
    };

    protected override IReadOnlyList<string> GetAvailableFormats(string extension) => extension switch
    {
        "iso" => ["CSO", "CHD"],
        "cso" => ["ISO", "CHD"],
        "chd" => ["ISO", "CSO"],
        _ => []
    };

    protected override string FormatSize(long bytes) => PickPack.Disk.ETC.FileSize.FormatSize(bytes);
}