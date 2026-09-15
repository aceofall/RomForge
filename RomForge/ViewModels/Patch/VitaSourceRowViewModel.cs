using Common.WPF.ViewModels;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vita.Core.Models;
using Vita.Core.Services;

namespace RomForge.ViewModels.Patch;

public class VitaSourceRowViewModel : ViewModelBase
{
    private string _license = string.Empty;
    private string _patchPath = string.Empty;
    private VitaContentCategory _category;
    private string? _errorMessage;
    private bool _isLicenseEditable = true;
    private long _sizeBytes = -1;
    private byte[]? _iconBytes;
    private string? _gameName;
    private CancellationTokenSource? _pkgIconLoadCts;

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
        set
        {
            _license = value;
            OnPropertyChanged();

            if (Kind == VitaSourceKind.Pkg && Category == VitaContentCategory.App)
                _ = DebounceLoadPkgIconAsync(value);
        }
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

    public bool IsLicenseEditable
    {
        get => _isLicenseEditable;
        set { _isLicenseEditable = value; OnPropertyChanged(); }
    }

    public long SizeBytes
    {
        get => _sizeBytes;
        set { _sizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Size)); }
    }

    public string Size => SizeBytes < 0 ? "계산 중..." : FormatBytes(SizeBytes);

    public byte[]? IconBytes
    {
        get => _iconBytes;
        set { _iconBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(IconImageSource)); }
    }

    public ImageSource? IconImageSource
    {
        get
        {
            if (_iconBytes == null)
                return null;

            var bitmap = new BitmapImage();

            using var ms = new MemoryStream(_iconBytes);

            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
    }

    public string? GameName
    {
        get => _gameName;
        set { _gameName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(GameName) ? FileName : GameName!;

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

    public async Task LoadMetadataAsync()
    {
        await RefreshSizeAsync();

        if (Category == VitaContentCategory.App && Kind == VitaSourceKind.ZipOrFolder)
            await LoadIconAndTitleFromContainerAsync();
    }

    private async Task RefreshSizeAsync()
    {
        try
        {
            long bytes = await Task.Run(() =>
            {
                if (Kind == VitaSourceKind.Pkg)
                    return new FileInfo(Path).Length;

                using var accessor = VitaSourceAccessorFactory.Open(Path);
                string prefix = string.IsNullOrEmpty(ItemSourcePath) ? string.Empty : ItemSourcePath.Replace('\\', '/').Trim('/') + "/";
                long total = 0;

                foreach (var file in accessor.EnumerateAllFiles())
                {
                    string normalized = file.Replace('\\', '/');

                    if (prefix.Length == 0 || normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        total += accessor.GetFileSize(file);
                }

                return total;
            });

            SizeBytes = bytes;
        }
        catch
        {
            SizeBytes = 0;
        }
    }

    private async Task LoadIconAndTitleFromContainerAsync()
    {
        try
        {
            var result = await Task.Run(() =>
            {
                using var accessor = VitaSourceAccessorFactory.Open(Path);
                string iconRel = $"{ItemSourcePath}/sce_sys/icon0.png";
                string sfoRel = $"{ItemSourcePath}/sce_sys/param.sfo";
                byte[]? icon = accessor.FileExists(iconRel) ? accessor.ReadAllBytes(iconRel) : null;
                string? title = null;

                if (accessor.FileExists(sfoRel))
                {
                    var sfo = VitaSfoParser.Parse(accessor.ReadAllBytes(sfoRel));
                    title = VitaSfoParser.GetString(sfo, "TITLE");
                }

                return (icon, title);
            });

            IconBytes = result.icon;
            GameName = result.title;
        }
        catch
        {
            IconBytes = null;
            GameName = null;
        }
    }

    private async Task DebounceLoadPkgIconAsync(string license)
    {
        _pkgIconLoadCts?.Cancel();

        var cts = new CancellationTokenSource();

        _pkgIconLoadCts = cts;

        try
        {
            await Task.Delay(500, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
            return;

        if (string.IsNullOrWhiteSpace(license))
        {
            IconBytes = null;
            GameName = null;
            return;
        }

        try
        {
            var result = await Task.Run(() =>
            {
                using var accessor = new PkgSourceAccessor(Path, license);
                byte[]? icon = accessor.FileExists("sce_sys/icon0.png") ? accessor.ReadAllBytes("sce_sys/icon0.png") : null;
                string? title = null;

                if (accessor.FileExists("sce_sys/param.sfo"))
                {
                    var sfo = VitaSfoParser.Parse(accessor.ReadAllBytes("sce_sys/param.sfo"));
                    title = VitaSfoParser.GetString(sfo, "TITLE");
                }

                return (icon, title);
            }, cts.Token);

            if (cts.IsCancellationRequested)
                return;

            IconBytes = result.icon;
            GameName = result.title;
        }
        catch
        {
            if (!cts.IsCancellationRequested)
            {
                IconBytes = null;
                GameName = null;
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
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