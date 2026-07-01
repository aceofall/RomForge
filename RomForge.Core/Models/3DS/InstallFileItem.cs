using Common.WPF.ViewModels;
using System.Windows.Media;

namespace RomForge.Core.Models._3DS;

public class InstallFileItem(string filePath) : FileItemBase(filePath)
{
    public string ExtensionLabel => Extension;

    public Brush ExtensionBackground { get; } = new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7));

    protected override string FormatSize(long bytes) => PickPack.Disk.ETC.FileSize.FormatSize(bytes);
}