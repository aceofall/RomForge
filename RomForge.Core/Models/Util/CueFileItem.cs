using Common.WPF.ViewModels;

namespace RomForge.Core.Models.Util;

public class CueFileItem(string filePath) : FileItemBase(filePath)
{
    public string TargetName => $"{FileName}.cue";

    protected override string FormatSize(long bytes) => PickPack.Disk.ETC.FileSize.FormatSize(bytes);
}