using Common.WPF.ViewModels;
using System.IO;
using System.Windows.Media;
using Vita.Core.Models;
using Vita.Core.Services;

namespace RomForge.ViewModels.Patch;

public class VitaSourceRowViewModel : ViewModelBase
{
    private string _license = string.Empty;
    private string _patchPath = string.Empty;
    private VitaContentCategory _category;
    private string? _errorMessage;

    public string Path { get; }

    public VitaSourceKind Kind { get; }

    public string? ItemSourcePath { get; }

    public string FileName => Kind == VitaSourceKind.Pkg
        ? System.IO.Path.GetFileName(Path)
        : $"{System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))} :: {ItemSourcePath}";

    public bool IsPkg => Kind == VitaSourceKind.Pkg;

    public string TitleId { get; }

    public string? ContentIdSuffix { get; }

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

    private VitaSourceRowViewModel(string path, VitaSourceKind kind, string? itemSourcePath, string titleId, VitaContentCategory category, string? contentIdSuffix)
    {
        Path = path;
        Kind = kind;
        ItemSourcePath = itemSourcePath;
        TitleId = titleId;
        _category = category;
        ContentIdSuffix = contentIdSuffix;
    }

    public static VitaSourceRowViewModel FromPkg(string pkgPath)
    {
        var result = VitaPkgProbe.Probe(pkgPath);

        return new VitaSourceRowViewModel(pkgPath, VitaSourceKind.Pkg, null, result.TitleId, result.Category, result.ContentIdSuffix);
    }

    public static VitaSourceRowViewModel FromZipItem(string containerPath, VitaSourceItem item) =>
        new(containerPath, VitaSourceKind.ZipOrFolder, item.SourcePath, item.TitleId, item.Category, item.ContentIdSuffix);

    public static List<VitaSourceRowViewModel> DiscoverFromContainer(string containerPath)
    {
        using var accessor = VitaSourceAccessorFactory.Open(containerPath);
        var items = VitaSourcePreparer.DiscoverItems(accessor);

        if (items.Count == 0)
            throw new InvalidDataException("app/patch/addcont 폴더를 찾을 수 없습니다.");

        return [.. items.Select(item => FromZipItem(containerPath, item))];
    }

    public VitaBatchSourceEntry ToBatchEntry() => Kind == VitaSourceKind.Pkg
        ? new VitaBatchSourceEntry
        {
            Kind = VitaSourceKind.Pkg,
            Path = Path,
            License = License,
            PatchPath = string.IsNullOrWhiteSpace(PatchPath) ? null : PatchPath,
            Probe = new VitaPkgProbeResult { TitleId = TitleId, Category = Category, ContentIdSuffix = ContentIdSuffix, ContentId = string.Empty }
        }
        : new VitaBatchSourceEntry
        {
            Kind = VitaSourceKind.ZipOrFolder,
            Path = Path,
            PatchPath = string.IsNullOrWhiteSpace(PatchPath) ? null : PatchPath,
            ItemCategory = Category,
            ItemTitleId = TitleId,
            ItemContentIdSuffix = ContentIdSuffix,
            ItemSourcePath = ItemSourcePath
        };
}