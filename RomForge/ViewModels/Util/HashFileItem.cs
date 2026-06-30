using Common.WPF.ViewModels;
using System.IO;

namespace RomForge.ViewModels.Util;

public class HashFileItem(string filePath) : FileItemBase(filePath)
{
    private string _hashResult = string.Empty;

    public string RawHash { get; set; } = string.Empty;

    public string HashResult
    {
        get => _hashResult;
        set => SetProperty(ref _hashResult, value);
    }

    public override string FileName => Path.GetFileName(FilePath);

    protected override string FormatSize(long bytes) => PickPack.Disk.ETC.FileSize.FormatSize(bytes);
}