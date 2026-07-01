using Common;
using Common.WPF.ViewModels;
using NSW.WPF.Services;
using Patch.Core;
using RomForge.Models;
using RomForge.ViewModels.Patch.Services;
using RomZip.Core.Services;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels.Patch;

public class NormalPatchMainViewModel : ToolTabViewModel, IPatchViewModel
{
    private readonly Core.AppConfig _config;
    private CancellationTokenSource? _runCts;

    public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];

    private string? _sourcePath;
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

    private string? _patchPath;
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
        get => _config.Patch.AutoCompress;
        set
        {
            _config.Patch.AutoCompress = value;
            OnPropertyChanged(nameof(AutoCompress));
        }
    }

    public string SourceLabel => Path.GetFileName(SourcePath) ?? "원본 파일을 드래그하거나 클릭하세요";
    public string PatchLabel => Path.GetFileName(PatchPath) ?? "패치 파일을 드래그하거나 클릭하세요";

    private int _progress;
    public int Progress
    {
        get => _progress;
        set 
        { 
            _progress = value; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string ProgressText
    {
        get => $"{_progress}%";
    }

    private string _progressStatus = "대기 중";
    public string ProgressStatus
    {
        get => _progressStatus;
        set { _progressStatus = value; OnPropertyChanged(); }
    }

    public NormalPatchMainViewModel(Core.AppConfig config)
    {
        _config = config;

        _config.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Core.AppConfig.Patch))
                OnPropertyChanged(nameof(AutoCompress));
        };

        _config.Patch.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Core.PatchConfig.AutoCompress))
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

        Progress = 0;
        ProgressStatus = "패치 중...";

        string outputDir = Path.Combine(Path.GetDirectoryName(SourcePath)!, "output");
        string outputPath = Path.Combine(outputDir, Path.GetFileName(SourcePath));
        outputPath = Utils.GetUniqueFilePath(outputPath);

        Log($"패치 시작: {Path.GetFileName(SourcePath)}", LogLevel.Highlight);

        var orchestrator = new PatchOrchestrator(
            Log,
            p => Progress = p,
            s => ProgressStatus = s,
            AutoCompress,
            _config.Dolphin.CompressLevel);

        try
        {
            Directory.CreateDirectory(outputDir);

            var detected = FormatDetector.Detect(SourcePath);
            var sourceLength = new FileInfo(SourcePath).Length;
            var patchLength = new FileInfo(PatchPath).Length;
            bool useBytes = sourceLength < UniversalPatcher.MemoryThreshold && patchLength < UniversalPatcher.MemoryThreshold;

            await orchestrator.PatchAsync(SourcePath, PatchPath, detected, outputDir, outputPath, useBytes, ct);

            ProgressStatus = "완료";

            outputDir.OpenFolder();
        }
        catch (OperationCanceledException)
        {
            Progress = 0;
            ProgressStatus = "취소됨";
            Log($"패치 취소: {SourcePath}", LogLevel.Error);

            orchestrator.Cleanup(outputPath);
        }
        catch (Exception ex)
        {
            Progress = 0;
            ProgressStatus = "실패";
            Log($"패치 실패: {ex.Message}", LogLevel.Error);

            orchestrator.Cleanup(outputPath);
        }
    }

    public void Cancel() => _runCts?.Cancel();

    public void Clear()
    {
        _runCts?.Cancel();

        SourcePath = null;
        PatchPath = null;
        Progress = 0;
        ProgressStatus = "대기 중";
        AutoCompress = false;

        LogEntries.Clear();
    }
}