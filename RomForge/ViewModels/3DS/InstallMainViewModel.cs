using _3DS.Core.Crypto;
using _3DS.Core.Enums;
using _3DS.Core.IO;
using _3DS.Core.Models;
using _3DS.Core.Services;
using Common.WPF.ViewModels;
using LibHac.Tools.Fs;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomForge.ViewModels._3DS;

public class InstallMainViewModel : ToolTabViewModel
{
    private static readonly HashSet<string> AllowedExtensions = [".3ds", ".cia", ".cci", ".zcci"];

    private bool _isInstalling;

    public ObservableCollection<TitleViewModel> Items { get; } = [];
    public Visibility HintVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool IsNotInstalling => !IsInstalling;

    public bool IsInstalling
    {
        get => _isInstalling;
        set { _isInstalling = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotInstalling)); }
    }

    public async Task AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            string fullPath = Path.GetFullPath(path);
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();

            if (!AllowedExtensions.Contains(ext) || Items.Any(f => f.FilePath == fullPath))
                continue;

            try
            {
                var result = await Util.ParseFile(fullPath);
                var vm = new TitleViewModel()
                {
                    Title = result.Title!,
                    FilePath = path,
                    ProductCode = result.ProductCode,
                    ShortDescription = result.ShortDescription,
                    Publisher = result.Publisher,
                    Crypto = result.Crypto
                };

                if (result?.IconPixels is not null)
                {
                    var bitmap = BitmapSource.Create(48, 48, 96, 96, PixelFormats.Bgr32, null, result?.IconPixels, 48 * 4);
                    bitmap.Freeze();
                    vm.Icon = bitmap;
                }

                Items.Add(vm);
                OnPropertyChanged(nameof(HintVisibility));
            }
            catch { }
        }
    }

    public void RemoveItems(IEnumerable<TitleViewModel> items)
    {
        foreach (var item in items.ToList())
            Items.Remove(item);

        OnPropertyChanged(nameof(HintVisibility));
    }

    public void Clear()
    {
        Items.Clear();
        OnPropertyChanged(nameof(HintVisibility));
    }
}