using Common.WPF.ViewModels;
using System.Windows.Media;
using Vita.Core.Models;
using Vita.Core.Services;

namespace RomForge.ViewModels.Patch;

public class VitaPkgRowViewModel(string pkgPath) : ViewModelBase
{
    private string _license = string.Empty;
    private string _patchPath = string.Empty;
    private VitaContentCategory _category;
    private string? _errorMessage;

    public string PkgPath { get; } = pkgPath;

    public string FileName => System.IO.Path.GetFileName(PkgPath);

    public string TitleId { get; private set; } = string.Empty;

    public string? ContentIdSuffix { get; private set; }    

    public VitaContentCategory Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); }
    }

    public string License
    {
        get => _license;
        set { _license = value; OnPropertyChanged(); }
    }

    public string PatchPath
    {
        get => _patchPath;
        set { _patchPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(PatchIconSource)); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsValid)); }
    }

    public bool IsValid => ErrorMessage is null;

    public string PatchIconSource => string.IsNullOrEmpty(PatchPath) ? "/Assets/Images/NoPatch.png" : "/Assets/Images/Patch.png";

    public SolidColorBrush CategoryBadgeColor => Category switch
    {
        VitaContentCategory.App => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
        VitaContentCategory.Patch => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
        VitaContentCategory.Addcont => new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
        _ => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
    };

    public void Probe()
    {
        try
        {
            var result = VitaPkgProbe.Probe(PkgPath);

            TitleId = result.TitleId;
            Category = result.Category;
            ContentIdSuffix = result.ContentIdSuffix;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        OnPropertyChanged(nameof(TitleId));
        OnPropertyChanged(nameof(ContentIdSuffix));
        OnPropertyChanged(nameof(Category));
    }

    public VitaPkgBatchEntry ToBatchEntry() => new()
    {
        PkgPath = PkgPath,
        License = License,
        PatchPath = PatchPath,
        Probe = new VitaPkgProbeResult
        {
            TitleId = TitleId,
            Category = Category,
            ContentIdSuffix = ContentIdSuffix,
            ContentId = string.Empty
        }
    };
}