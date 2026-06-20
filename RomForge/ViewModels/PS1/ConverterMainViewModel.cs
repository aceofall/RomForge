using Common;
using Common.WPF.ViewModels;
using PBP.Core.Models;
using PBP.Core.Services;
using RomForge.Core.Services.PS1;
using RomForge.Helpers;
using RomForge.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace RomForge.ViewModels.PS1;

public class ConverterMainViewModel : ToolTabViewModel
{
    private const int MaxItems = 8;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".m3u", ".iso", ".chd"
    };

    private bool _isConverting;
    public bool IsConverting
    {
        get => _isConverting;
        set { _isConverting = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _gameTitle;
    public string GameTitle
    {
        get => _gameTitle;
        set { _gameTitle = value; OnPropertyChanged(); }
    }    

    private CancellationTokenSource _cts = new();
    private string? _lastIconGameId;
    private CancellationTokenSource? _iconCts;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<DiscFileItem> FileItems { get; } = [];

    public Visibility HintVisibility => FileItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private BitmapImage? _icon0Image;
    public BitmapImage? Icon0Image { get => _icon0Image; set { _icon0Image = value; OnPropertyChanged(); } }

    private BitmapImage? _pic0Image;
    public BitmapImage? Pic0Image { get => _pic0Image; set { _pic0Image = value; OnPropertyChanged(); } }

    private BitmapImage? _pic1Image;
    public BitmapImage? Pic1Image { get => _pic1Image; set { _pic1Image = value; OnPropertyChanged(); } }

    private byte[] _icon0Bytes = PbpResources.ICON0;
    private byte[] _pic0Bytes = PbpResources.PIC0;
    private byte[] _pic1Bytes = PbpResources.PIC1;

    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }

    public ConverterMainViewModel()
    {
        RunCommand = new RelayCommand(async _ => await RunAsync(), _ => !IsConverting && FileItems.Count > 0);
        CancelCommand = new RelayCommand(_ => _cts.Cancel(), _ => IsConverting);

        Icon0Image = BytesToImage(_icon0Bytes);
        Pic0Image = BytesToImage(_pic0Bytes);
        Pic1Image = BytesToImage(_pic1Bytes);
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var existing = FileItems.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = ExpandPaths(paths)
            .Where(p => SupportedExtensions.Contains(Path.GetExtension(p)))
            .Where(p => existing.Add(p))
            .ToList();

        var room = MaxItems - FileItems.Count;
        var toAdd = candidates.Take(Math.Max(room, 0)).ToList();
        var rejected = candidates.Count - toAdd.Count;

        foreach (var path in toAdd)
        {
            var item = new DiscFileItem(path);
            FileItems.Add(item);
            _ = LoadItemInfoAsync(item);
        }

        if (rejected > 0)
        {
            MessageBox.Show($"최대 {MaxItems}개까지만 추가할 수 있어요. {rejected}개 파일은 추가되지 않았어요.",
                "추가 제한", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        OnPropertyChanged(nameof(HintVisibility));
        CommandManager.InvalidateRequerySuggested();
    }

    public void RemoveItems(IEnumerable<DiscFileItem> items)
    {
        foreach (var item in items.ToList())
            FileItems.Remove(item);

        OnPropertyChanged(nameof(HintVisibility));
        ResortAndRenumber();
    }

    public void ClearItems()
    {
        FileItems.Clear();
        OnPropertyChanged(nameof(HintVisibility));
        _lastIconGameId = null;
        Icon0Image = BytesToImage(PbpResources.ICON0);
    }

    public void SetIcon0FromFile(string path) => SetImage(File.ReadAllBytes(path), bytes => { _icon0Bytes = bytes; Icon0Image = BytesToImage(bytes); });
    public void SetPic0FromFile(string path) => SetImage(File.ReadAllBytes(path), bytes => { _pic0Bytes = bytes; Pic0Image = BytesToImage(bytes); });
    public void SetPic1FromFile(string path) => SetImage(File.ReadAllBytes(path), bytes => { _pic1Bytes = bytes; Pic1Image = BytesToImage(bytes); });

    private static void SetImage(byte[] rawBytes, Action<byte[]> apply)
    {
        try { apply(ImageConversion.ToPng(rawBytes)); }
        catch { /* 이미지가 아니면 그냥 무시 */ }
    }

    private async Task LoadItemInfoAsync(DiscFileItem item)
    {
        try
        {
            var (gameId, size) = await Task.Run(() =>
            {
                var ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
                var size = DiscSizeResolver.GetTotalSize(item.FilePath);

                DiskSource source = ext switch
                {
                    ".cue" => DiskSource.FromBinCue(CueFileResolver.GetBinPath(item.FilePath), item.FilePath),
                    ".chd" => DiskSource.FromChd(item.FilePath),
                    ".m3u" => DiskSource.FromIso(ResolveM3uFirstDisc(item.FilePath)),
                    _ => DiskSource.FromIso(item.FilePath)
                };

                var gameId = GameIdReader.ReadFromDisk(source);

                return (gameId, size);
            });

            item.GameId = gameId;
            item.FileSizeBytes = size;
        }
        catch (Exception ex)
        {
            item.GameId = "인식실패";
            AppendLog($"[{item.FileName}] GameID 인식 실패: {ex.Message}", LogLevel.Error);
        }

        ResortAndRenumber();
    }

    private static string ResolveM3uFirstDisc(string m3uPath)
    {
        var dir = Path.GetDirectoryName(m3uPath)!;
        var firstLine = File.ReadAllLines(m3uPath)
            .Select(l => l.Trim())
            .First(l => l.Length > 0 && !l.StartsWith('#'));

        var fullPath = Path.IsPathRooted(firstLine) ? firstLine : Path.Combine(dir, firstLine);

        return Path.GetExtension(fullPath).Equals(".cue", StringComparison.OrdinalIgnoreCase)
            ? CueFileResolver.GetBinPath(fullPath)
            : fullPath;
    }

    private void ResortAndRenumber()
    {
        var sorted = FileItems.OrderBy(i => i.GameId, StringComparer.OrdinalIgnoreCase).ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i].No = i + 1;
            var oldIndex = FileItems.IndexOf(sorted[i]);
            if (oldIndex != i) FileItems.Move(oldIndex, i);
        }

        _ = UpdateImageAsync();
    }

    private async Task UpdateImageAsync()
    {
        var primary = FileItems.FirstOrDefault(i => i.No == 1);

        if (primary == null || primary.GameId == _lastIconGameId || primary.GameId is "인식중..." or "인식실패")
            return;

        _lastIconGameId = primary.GameId;
        _iconCts?.Cancel();
        _iconCts = new CancellationTokenSource();
        var ct = _iconCts.Token;

        var icon0Png = await CoverArtFetcher.TryDownloadIconPngAsync(primary.GameId, ct);

        if (ct.IsCancellationRequested) 
            return;

        _icon0Bytes = icon0Png ?? PbpResources.ICON0;
        Icon0Image = BytesToImage(_icon0Bytes);

        var meta = GameMetadataLookup.Find(primary.GameId);

        var pic0Png = meta != null ? await GameMetadataLookup.TryDownloadImagePngAsync(meta.Pic0, ct) : null;

        if (ct.IsCancellationRequested) 
            return;

        _pic0Bytes = pic0Png ?? PbpResources.PIC0;
        Pic0Image = BytesToImage(_pic0Bytes);

        var pic1Png = meta != null ? await GameMetadataLookup.TryDownloadImagePngAsync(meta.Pic1, ct) : null;

        if (ct.IsCancellationRequested) 
            return;

        _pic1Bytes = pic1Png ?? PbpResources.PIC1;
        Pic1Image = BytesToImage(_pic1Bytes);

        if (meta != null && !string.IsNullOrWhiteSpace(meta.Title))
            GameTitle = meta.Title;
    }

    private static BitmapImage BytesToImage(byte[] bytes)
    {
        var image = new BitmapImage();
        using var ms = new MemoryStream(bytes);

        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();

        return image;
    }

    private async Task RunAsync()
    {
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        IsConverting = true;
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
        };

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                foreach (var f in Directory.EnumerateFiles(path, "*.*", opts)) yield return f;
            else if (File.Exists(path))
                yield return path;
        }
    }

    private void AppendLog(string msg, LogLevel level = LogLevel.Info)
    {
        if (Application.Current?.Dispatcher == null) return;
        Application.Current.Dispatcher.Invoke(() => LogEntries.Add(new LogEntry { Message = msg, Level = level }));
    }

    public static string GetFileDialogFilter()
    {
        var wildcards = string.Join(";", SupportedExtensions.Select(ext => $"*{ext}"));
        return $"지원 파일|{wildcards}|모든 파일|*.*";
    }
}