using CHD.Core.Services;
using Common;
using Common.WPF.ViewModels;
using NSW.WPF.Services;
using Patch.Core.Formats.DCP.Services;
using RomForge.Core.Models;
using RomForge.Core.Services.Patch;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels.Patch;

public class DreamcastPatchMainViewModel : ToolTabViewModel, IPatchViewModel
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

    public string SourceLabel => Path.GetFileName(SourcePath) ?? "원본 GDI를 드래그하거나 클릭하세요";
    public string PatchLabel => Path.GetFileName(PatchPath) ?? "DCP 패치를 드래그하거나 클릭하세요";

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

    public string ProgressText => $"{_progress}%";

    private string _progressStatus = "대기 중";
    public string ProgressStatus
    {
        get => _progressStatus;
        set { _progressStatus = value; OnPropertyChanged(); }
    }

    public DreamcastPatchMainViewModel(Core.AppConfig config)
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

        string sourceDir = Path.GetDirectoryName(SourcePath)!;
        string outputDir = Path.Combine(sourceDir, "output", Path.GetFileNameWithoutExtension(SourcePath));

        Log($"드림캐스트 패치 시작: {Path.GetFileName(SourcePath)}", LogLevel.Highlight);

        try
        {
            Directory.CreateDirectory(outputDir);

            await DcpGdRomApplier.ApplyAsync(SourcePath, PatchPath, outputDir, (p, msg) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Progress = AutoCompress ? (int)(p * 50) : (int)(p * 100);
                    ProgressStatus = msg;
                });
            }, ct);

            Progress = AutoCompress ? 50 : 100;
            ProgressStatus = "패치 완료";
            Log($"패치 완료: {outputDir}", LogLevel.Highlight);

            string newGdiPath = Directory.GetFiles(outputDir, "*.gdi").First();

            if (AutoCompress)
            {
                ProgressStatus = "CHD 변환 중...";
                Log("CHD 변환 시작", LogLevel.Highlight);

                FileConverter converter = new();
                converter.LogMessage += (_, e) => Log(e.Message, e.Level);
                converter.ProgressChanged += (_, e) => Progress = 50 + (e.Progress / 2);

                var chdResult = await converter.ConvertFileAsync(newGdiPath, ct);

                if (!chdResult.Success)
                    throw new Exception($"CHD 변환 실패: {chdResult.Message}");

                Log($"CHD 변환 완료", LogLevel.Highlight);
            }

            Progress = 100;
            ProgressStatus = "완료";

            outputDir.OpenFolder();
        }
        catch (OperationCanceledException)
        {
            Progress = 0;
            ProgressStatus = "취소됨";
            Log($"패치 취소: {SourcePath}", LogLevel.Error);

            DeleteOutputDirectory(outputDir);
        }
        catch (Exception ex)
        {
            Progress = 0;
            ProgressStatus = "실패";
            Log($"패치 실패: {ex.Message}", LogLevel.Error);

            DeleteOutputDirectory(outputDir);
        }
    }

    private static void DeleteOutputDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch { }
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