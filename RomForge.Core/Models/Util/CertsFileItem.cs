using Common.WPF.ViewModels;

namespace RomForge.Core.Models.Util;

public class CertsFileItem(string filePath) : FileItemBase(filePath, "대기중")
{
    protected override string FormatSize(long bytes) => PickPack.Disk.ETC.FileSize.FormatSize(bytes);
}