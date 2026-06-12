using Common;
using Patch.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels;

/// <summary>
/// 기존 RomZip MainViewModel을 RomForge 네임스페이스로 이전.
/// RomZip.Core 프로젝트 참조 추가 후 서비스 연결.
/// </summary>
public class CompressViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".cue", ".gdi", ".chd",
        ".nsp", ".nsz", ".xci", ".xcz",
        ".gcm", ".wbfs", ".gcz", ".wia", ".rvz",
        ".3ds", ".cci", ".cia", ".zcci"
    };

    public static string GetFileDialogFilter()
    {
        string wildcards = string.Join(";", SupportedExtensions.Select(ext => $"*{ext}"));
        return $"지원 파일|{wildcards}|모든 파일|*.*";
    }

    #region Fields

    private bool _isConverting;
    private CancellationTokenSource _cts = new();
    private readonly AppConfig _config;

    #endregion

    #region Collections

    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<FileItemViewModel> FileItems { get; } = [];

    #endregion

    #region Properties

    public bool IsConverting
    {
        get => _isConverting;
        set { _isConverting = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public Visibility HintVisibility => FileItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    #endregion

    #region Commands

    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    public event Action<FileItemViewModel>? ScrollToItemRequested;

    public CompressViewModel(AppConfig config)
    {
        _config = config;
        RunCommand = new RelayCommand(async _ => await RunAsync(), _ => !IsConverting && FileItems.Count > 0);
        CancelCommand = new RelayCommand(_ => _cts.Cancel(), _ => IsConverting);
    }

    #region Public Methods

    public void AddPaths(IEnumerable<string> paths)
    {
        var existing = FileItems.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ExpandPaths(paths))
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(path))) continue;
            if (!existing.Add(path)) continue;

            var item = new FileItemViewModel(path) { No = FileItems.Count + 1 };
            FileItems.Add(item);
        }

        OnPropertyChanged(nameof(HintVisibility));
        CommandManager.InvalidateRequerySuggested();
    }

    public void RemoveItems(IEnumerable<FileItemViewModel> items)
    {
        foreach (var item in items.ToList()) FileItems.Remove(item);
        OnPropertyChanged(nameof(HintVisibility));
    }

    public void ClearItems()
    {
        FileItems.Clear();
        OnPropertyChanged(nameof(HintVisibility));
    }

    public static void OpenFolder(List<FileItemViewModel> selected)
    {
        if (selected.Count == 0) return;
        string path = Path.GetDirectoryName(selected[0].FilePath) ?? string.Empty;
        if (Directory.Exists(path))
            Process.Start("explorer.exe", $"\"{path}\"");
    }

    #endregion

    #region Private Methods

    private async Task RunAsync()
    {
        IsConverting = true;
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        LogEntries.Clear();

        try
        {
            AppendLog($"총 {FileItems.Count}개의 작업을 시작합니다.", LogLevel.Info);
            int cnt = 0;

            foreach (var item in FileItems)
            {
                if (_cts.Token.IsCancellationRequested) break;
                if (item.Status == "완료" || item.Status == "미지원") continue;

                item.Status = "변환중";
                item.Progress = 0;
                ScrollToItemRequested?.Invoke(item);

                var progress = new Progress<ProgressInfo>(p => item.Progress = p.Percent);

                // TODO: RomZip.Core 서비스 연결 (FormatDetector, 각 CompressService)
                await Task.Delay(100, _cts.Token); // placeholder

                item.Progress = 100;
                item.Status = "완료";
                cnt++;
            }

            if (cnt > 0)
                AppendLog($"총 {cnt}개의 작업을 완료했습니다.", LogLevel.Ok);
        }
        catch (OperationCanceledException)
        {
            AppendLog("취소되었습니다.", LogLevel.Error);
            foreach (var item in FileItems.Where(i => i.Status is "대기중" or "변환중"))
                item.Status = "취소";
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}", LogLevel.Error);
            foreach (var item in FileItems.Where(i => i.Status == "변환중"))
                item.Status = "실패";
        }
        finally
        {
            IsConverting = false;
        }
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
        => Application.Current.Dispatcher.Invoke(() => LogEntries.Add(new LogEntry { Message = msg, Level = level }));

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}