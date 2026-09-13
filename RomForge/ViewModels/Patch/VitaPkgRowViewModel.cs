using Common.WPF.ViewModels;
using Vita.Core.Models;
using Vita.Core.Services;

namespace RomForge.ViewModels.Patch;

public class VitaPkgRowViewModel(string pkgPath) : ViewModelBase
{
    private string _license = string.Empty;
    private string _patchPath = string.Empty;
    private VitaContentCategory _category;
    private string? _errorMessage;
    private string _patchDisplay;
    private string _patchIconSource;

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

    public string PatchIconSource => string.IsNullOrEmpty(PatchPath)
    ? "/Assets/Images/NoPatch.png"
    : "/Assets/Images/Patch.png";

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