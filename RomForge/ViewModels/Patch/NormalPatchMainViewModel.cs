using Common;
using Common.WPF.ViewModels;
using NSW.WPF.Services;
using Patch.Core;
using RomForge.Core;
using RomForge.Core.Models;
using RomForge.Core.Services.Compression;
using RomForge.Core.Services.Patch;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels.Patch;

public class NormalPatchMainViewModel : ToolTabViewModel, IPatchViewModel
{
    private CancellationTokenSource? _runCts;
    private string? _sourcePath;
    private string? _patchPath;
    private int _progressPct;
    private string _progressLabel = string.Empty;
    private string _progressPercent = "0%";
    private string _progressTime = string.Empty;
    private string _progressSpeed = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
    
    public string? SourcePath
    {
        get => _sourcePath;
        set
        {
            _sourcePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    
    public string? PatchPath
    {
        get => _patchPath;
        set
        {
            _patchPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PatchLabel));
        }
    }

    public bool AutoCompress
    {
        get => AppConfig.Instance.Patch.AutoCompress;
        set
        {
            AppConfig.Instance.Patch.AutoCompress = value;
            OnPropertyChanged(nameof(AutoCompress));
        }
    }

    public string SourceLabel => Path.GetFileName(SourcePath) ?? "원본 파일을 드래그하거나 클릭하세요";

    public string PatchLabel => Path.GetFileName(PatchPath) ?? "패치 파일을 드래그하거나 클릭하세요";

    public int ProgressPct
    {
        get => _progressPct;
        set { _progressPct = value; OnPropertyChanged(); }
    }

    public string ProgressLabel
    {
        get => _progressLabel;
        set { _progressLabel = value; OnPropertyChanged(); }
    }

    public string ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    public string ProgressTime
    {
        get => _progressTime;
        set { _progressTime = value; OnPropertyChanged(); }
    }

    public string ProgressSpeed
    {
        get => _progressSpeed;
        set { _progressSpeed = value; OnPropertyChanged(); }
    }

    public NormalPatchMainViewModel()
    {
        AppConfig.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AppConfig.Patch))
                OnPropertyChanged(nameof(AutoCompress));
        };

        AppConfig.Instance.Patch.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PatchConfig.AutoCompress))
                OnPropertyChanged(nameof(AutoCompress));
        };
    }

    public void Log(string message, LogLevel level)
    {
        Application.Current?.Dispatcher?.Invoke(() => LogEntries.Add(new LogEntry { Message = message, Level = level }));
    }

    public async Task RunAsync()
    {
        if (SourcePath is null || PatchPath is null)
            return;

        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        string outputDir = Path.Combine(Path.GetDirectoryName(SourcePath)!, "output");
        string outputPath = Path.Combine(outputDir, Path.GetFileName(SourcePath));
        outputPath = Utils.GetUniqueFilePath(outputPath);

        Log($"패치 시작: {Path.GetFileName(SourcePath)}", LogLevel.Highlight);

        var orchestrator = new PatchOrchestrator(Log, BuildProgressReporter(), AutoCompress, AppConfig.Instance.Dolphin.CompressLevel);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Directory.CreateDirectory(outputDir);

            var detected = FormatDetector.Detect(SourcePath);
            var sourceLength = new FileInfo(SourcePath).Length;
            var patchLength = new FileInfo(PatchPath).Length;
            bool useBytes = sourceLength < UniversalPatcher.MemoryThreshold && patchLength < UniversalPatcher.MemoryThreshold;

            await orchestrator.PatchAsync(SourcePath, PatchPath, detected, outputDir, outputPath, useBytes, ct);
            
            stopwatch.Stop();

            Log($"패치 완료: {Path.GetFileName(outputPath)} ({stopwatch.Elapsed:mm\\:ss})", LogLevel.Ok);

            outputDir.OpenFolder();
        }
        catch (OperationCanceledException)
        {            
            Log($"패치 취소: {SourcePath}", LogLevel.Error);
            CleanupTask();
            orchestrator.Cleanup(outputPath);
        }
        catch (Exception ex)
        {
            Log($"패치 실패: {ex.Message}", LogLevel.Error);
            CleanupTask();
            orchestrator.Cleanup(outputPath);
        }
    }

    private Progress<ProgressInfo> BuildProgressReporter() =>
        new(info =>
        {
            ProgressPct = info.Percent;
            ProgressLabel = info.Label;
            ProgressPercent = $"{info.Percent}%";
            ProgressTime = info.TimeInfo;
            ProgressSpeed = info.Speed;
        });

    private void CleanupTask()
    {
        ProgressPct = 0;
        ProgressLabel = string.Empty;
        ProgressPercent = "0%";
        ProgressTime = string.Empty;
        ProgressSpeed = string.Empty;
    }

    public void Cancel() => _runCts?.Cancel();

    public void Clear()
    {
        _runCts?.Cancel();

        SourcePath = null;
        PatchPath = null;
        AutoCompress = false;

        CleanupTask();

        LogEntries.Clear();
    }
}