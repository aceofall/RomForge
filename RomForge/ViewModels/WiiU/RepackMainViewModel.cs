using Common;
using Common.WPF.ViewModels;
using NSW.Core.Enums;
using NSW.WPF.Services;
using NSW.WPF.UI;
using RomForge.Core.Models;
using RomForge.Core.Services.WiiU;
using RomForge.Core.UI.Command;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WiiU.Core.Models;

namespace RomForge.ViewModels.WiiU;

public class RepackMainViewModel : ToolTabViewModel
{
    private CancellationTokenSource _cts = new();
    private BuildMode? _currentMode;
    private readonly RepackService _service;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    private string _inputPath = string.Empty;
    private string _keysPath = string.Empty;
    private string _patchPath = string.Empty;
    private string _outputPath = string.Empty;
    private int _progressPct;
    private string _progressLabel = "대기 중...";
    private string _progressPercent = string.Empty;
    private string _progressTime = "00:00 경과";
    private string _progressSpeed = string.Empty;
    private WiiUTitleInfo? _titleInfo;

    public string InputPath
    {
        get => _inputPath;
        set
        {
            _inputPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InputHintVisibility));
            OnPropertyChanged(nameof(KeysPathRequired));
            _ = RefreshTitleInfoAsync();
        }
    }

    public string KeysPath
    {
        get => _keysPath;
        set
        {
            _keysPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeysHintVisibility));
            _ = RefreshTitleInfoAsync();
        }
    }

    public string PatchPath
    {
        get => _patchPath;
        set { _patchPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(PatchHintVisibility)); }
    }

    public string OutputPath
    {
        get => _outputPath;
        set { _outputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputHintVisibility)); }
    }

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

    public WiiUTitleInfo? TitleInfo
    {
        get => _titleInfo;
        set { _titleInfo = value; OnPropertyChanged(); OnPropertyChanged(nameof(TitleInfoVisibility)); }
    }

    public Visibility InputHintVisibility => string.IsNullOrEmpty(InputPath) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility KeysHintVisibility => string.IsNullOrEmpty(KeysPath) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PatchHintVisibility => string.IsNullOrEmpty(PatchPath) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OutputHintVisibility => string.IsNullOrEmpty(OutputPath) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TitleInfoVisibility => TitleInfo != null ? Visibility.Visible : Visibility.Collapsed;

    public bool KeysPathRequired =>
        !string.IsNullOrEmpty(InputPath) &&
        !string.Equals(Path.GetExtension(InputPath), ".wua", StringComparison.OrdinalIgnoreCase);

    public bool IsUnpackRunning => IsLocked && _currentMode == BuildMode.UnpackOnly;

    public bool IsRebuildRunning => IsLocked && _currentMode == BuildMode.RebuildOnly;

    public bool IsFullRunning => IsLocked && _currentMode == BuildMode.FullProcess;

    public bool UnpackEnabled => !IsLocked || _currentMode == BuildMode.UnpackOnly;

    public bool RebuildEnabled => !IsLocked || _currentMode == BuildMode.RebuildOnly;

    public bool StartEnabled => !IsLocked || _currentMode == BuildMode.FullProcess;

    public ICommand BrowseInputCommand { get; }

    public ICommand BrowseKeysCommand { get; }

    public ICommand BrowsePatchCommand { get; }

    public ICommand BrowseOutputCommand { get; }

    public RepackMainViewModel()
    {
        _service = new RepackService(Log, () => PatchPath);

        OutputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        BrowseInputCommand = new RelayCommand(async _ => await BrowseInput());
        BrowseKeysCommand = new RelayCommand(async _ => await BrowseKeys());
        BrowsePatchCommand = new RelayCommand(async _ => await BrowsePatch());
        BrowseOutputCommand = new RelayCommand(async _ => await BrowseOutput());

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsLocked))
                NotifyButtonStates();
        };
    }

    public async Task StartAsync(BuildMode mode)
    {
        if (!Validate(mode, out string error))
        {
            Log(error, LogLevel.Error);
            return;
        }

        _currentMode = mode;
        NotifyButtonStates();

        using (BeginWork())
        {
            try
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();

                await ExecuteAsync(mode, _cts.Token);
            }
            finally
            {
                ProgressPct = 0;
                _currentMode = null;

                NotifyButtonStates();
            }
        }
    }

    public void Cancel() => _cts.Cancel();

    private async Task ExecuteAsync(BuildMode mode, CancellationToken ct)
    {
        string unpackedPath = Path.Combine(OutputPath, "unpacked");

        if (mode == BuildMode.UnpackOnly && Directory.Exists(unpackedPath))
        {
            if (!MessageBoxHelper.ShowQuestion("기존 언팩 데이터를 삭제하고 새로 진행할까요?"))
                return;

            Directory.Delete(unpackedPath, true);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var progress = BuildProgressReporter();
        bool isCompleted = false;

        try
        {
            Directory.CreateDirectory(OutputPath);

            switch (mode)
            {
                case BuildMode.UnpackOnly:
                    await _service.UnpackAsync(InputPath, unpackedPath, KeysPath, progress, ct);
                    break;
                case BuildMode.RebuildOnly:
                    await _service.RepackAsync(unpackedPath, OutputPath, progress, ct);
                    break;
                case BuildMode.FullProcess:
                    await _service.RepackDirectAsync(InputPath, KeysPath, OutputPath, progress, ct);
                    break;
            }

            isCompleted = true;
            ProgressPercent = "100%";

            Log($"완료! 총 소요: {sw.Elapsed:mm\\:ss}", LogLevel.Ok);
            OutputPath.OpenFolder();
        }
        catch (OperationCanceledException)
        {
            Log("작업이 취소되었습니다.", LogLevel.Error);
        }
        catch (Exception ex)
        {
            Log($"오류: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!isCompleted && mode == BuildMode.UnpackOnly && Directory.Exists(unpackedPath))
            {
                try { Directory.Delete(unpackedPath, true); } catch { }
            }
        }
    }

    private Action<ProgressInfo> BuildProgressReporter() =>
        info =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProgressPct = info.Percent;
                ProgressLabel = info.Label;
                ProgressPercent = $"{info.Percent}%";
                ProgressTime = info.TimeInfo;
                ProgressSpeed = info.Speed;
            });
        };

    private async Task RefreshTitleInfoAsync()
    {
        TitleInfo = null;

        if (string.IsNullOrEmpty(InputPath) || !File.Exists(InputPath)) 
            return;

        bool isWua = string.Equals(Path.GetExtension(InputPath), ".wua", StringComparison.OrdinalIgnoreCase);

        if (!isWua && (string.IsNullOrEmpty(KeysPath) || !File.Exists(KeysPath)))
            return;

        await Task.Run(() =>
        {
            try
            {
                using var source = UnpackService.Open(InputPath, isWua ? null : KeysPath);
                int count = 0;

                foreach (var _ in source.EnumerateFiles()) 
                    count++;

                var info = new WiiUTitleInfo { TitleIdHex = source.TitleIdHex, TitleVersion = source.TitleVersion, FileCount = count };

                Application.Current.Dispatcher.Invoke(() => TitleInfo = info);
            }
            catch
            {
            }
        });
    }

    private bool Validate(BuildMode mode, out string error)
    {
        error = string.Empty;

        if (mode != BuildMode.RebuildOnly && string.IsNullOrEmpty(InputPath))
        {
            error = "원본 파일을 선택하세요.";
            return false;
        }

        if (mode != BuildMode.RebuildOnly && KeysPathRequired && string.IsNullOrEmpty(KeysPath))
        {
            error = "keys.txt를 선택하세요 (wud/wux 입력에는 필요합니다).";
            return false;
        }

        if (string.IsNullOrEmpty(OutputPath))
        {
            error = "작업 폴더를 선택하세요.";
            return false;
        }

        if (mode == BuildMode.RebuildOnly)
        {
            string unpackedPath = Path.Combine(OutputPath, "unpacked");
            if (!Directory.Exists(unpackedPath))
            {
                error = "언팩된 데이터가 없습니다.";
                return false;
            }
        }

        return true;
    }

    private void NotifyButtonStates()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(IsUnpackRunning));
            OnPropertyChanged(nameof(IsRebuildRunning));
            OnPropertyChanged(nameof(IsFullRunning));
            OnPropertyChanged(nameof(UnpackEnabled));
            OnPropertyChanged(nameof(RebuildEnabled));
            OnPropertyChanged(nameof(StartEnabled));
        });
    }

    private void Log(string msg, LogLevel level = LogLevel.Info) => Application.Current.Dispatcher.Invoke(() => LogEntries.Add(new LogEntry { Message = msg, Level = level }));

    private async Task BrowseInput()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "원본 파일 선택",
            Filter = "Wii U ROM 파일|*.wud;*.wux;*.wua"
        };

        if (dlg.ShowDialog() == true)
            InputPath = dlg.FileName;
    }

    private async Task BrowseKeys()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "keys.txt 선택",
            Filter = "keys.txt|*.txt|모든 파일|*.*"
        };

        if (dlg.ShowDialog() == true)
            KeysPath = dlg.FileName;
    }

    private async Task BrowsePatch()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "한글패치 폴더 선택" };

        if (dlg.ShowDialog() == true)
            PatchPath = dlg.FolderName;
    }

    private async Task BrowseOutput()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "작업 폴더 선택" };

        if (dlg.ShowDialog() == true)
            OutputPath = dlg.FolderName;
    }
}