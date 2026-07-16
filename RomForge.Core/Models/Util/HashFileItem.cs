using Common.WPF.ViewModels;
using System.IO;

namespace RomForge.Core.Models.Util;

public class HashFileItem(string filePath) : FileItemBase(filePath)
{
    private string _hashResult = string.Empty;

    public string RawHash { get; set; } = string.Empty;

    public string HashResult
    {
        get => _hashResult;
        set
        {
            if (SetProperty(ref _hashResult, value))
                OnPropertyChanged(nameof(IsHashAvailable));
        }
    }

    public bool IsHashAvailable => !string.IsNullOrEmpty(HashResult);

    public override string FileName => Path.GetFileName(FilePath);

    protected override string FormatSize(long bytes) => PickPack.Disk.ETC.FileSize.FormatSize(bytes);
}