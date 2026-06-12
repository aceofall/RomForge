using Common;
using Patch.Core;
using Patch.Core.Models;
using Patch.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels;

public class PatchViewModel : INotifyPropertyChanged
{
    private string? _sourcePath;
    private string? _patchPath;
    private bool _isPatching;
    private CancellationTokenSource _cts = new();

    public ObservableCollection<PatchItemViewModel> PairItems { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public string? SourcePath
    {
        get => _sourcePath;
        set { _sourcePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceLabel)); TryMatch(); }
    }

    public string? PatchPath
    {
        get => _patchPath;
        set { _patchPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(PatchLabel)); TryMatch(); }
    }

    public string SourceLabel => _sourcePath != null ? Path.GetFileName(_sourcePath) : "원본 파일 또는 ZIP을 드래그하거나 클릭하세요";
    public string PatchLabel  => _patchPath  != null ? Path.GetFileName(_patchPath)  : "패치 파일, 폴더 또는 ZIP을 드래그하거나 클릭하세요";

    public bool IsPatching
    {
        get => _isPatching;
        set { _isPatching = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public Visibility HintVisibility => PairItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public ICommand RunCommand    { get; }
    public ICommand CancelCommand { get; }

    private readonly AppConfig _config;

    public PatchViewModel(AppConfig config)
    {
        _config = config;
        RunCommand    = new RelayCommand(async _ => await RunAsync(), _ => !IsPatching && PairItems.Any(p => p.PairStatus == PairStatus.Matched));
        CancelCommand = new RelayCommand(_ => _cts.Cancel(), _ => IsPatching);
    }

    private void TryMatch()
    {
        PairItems.Clear();
        OnPropertyChanged(nameof(HintVisibility));

        if (_sourcePath == null || _patchPath == null) return;

        var pairs = PatchMatcher.Match(_sourcePath, _patchPath);
        int no = 1;
        foreach (var pair in pairs)
            PairItems.Add(new PatchItemViewModel(pair, no++));

        OnPropertyChanged(nameof(HintVisibility));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunAsync()
    {
        IsPatching = true;
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        LogEntries.Clear();

        var matched = PairItems.Where(p => p.PairStatus == PairStatus.Matched).ToList();
        AppendLog($"총 {matched.Count}개 패치 작업을 시작합니다.", LogLevel.Info);

        int done = 0;
        try
        {
            foreach (var item in matched)
            {
                if (_cts.Token.IsCancellationRequested) break;

                item.Status   = "패치중";
                item.Progress = 0;

                var progress = new Progress<ProgressInfo>(p => item.Progress = p.Percent);

                await PatchService.ApplyAsync(
                    item.SourcePath,
                    item.PatchPath,
                    _config.Patch,
                    progress,
                    AppendLog,
                    _cts.Token);

                item.Progress = 100;
                item.Status   = "완료";
                done++;
            }

            if (done > 0)
                AppendLog($"총 {done}개 완료.", LogLevel.Ok);
        }
        catch (OperationCanceledException)
        {
            AppendLog("취소되었습니다.", LogLevel.Error);
            foreach (var item in matched.Where(i => i.Status is "패치중" or "대기중"))
                item.Status = "취소";
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}", LogLevel.Error);
            foreach (var item in matched.Where(i => i.Status == "패치중"))
                item.Status = "실패";
        }
        finally
        {
            IsPatching = false;
        }
    }

    private void AppendLog(string msg, LogLevel level = LogLevel.Info)
        => System.Windows.Application.Current.Dispatcher.Invoke(() => LogEntries.Add(new LogEntry { Message = msg, Level = level }));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
