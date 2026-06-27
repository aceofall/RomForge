using _3DS.Core.Crypto;
using _3DS.Core.Interfaces;
using _3DS.Core.Models;
using _3DS.Core.Services;
using Common;
using Common.WPF.ViewModels;
using NSW.Core.Enums;
using NSW.WPF.Services;
using RomForge.Helpers;
using RomForge.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels._3DS;

public class RepackMainViewModel : ToolTabViewModel
{
    private CancellationTokenSource _cts = new();
    private BuildMode? _currentMode;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    private string _inputPath = string.Empty;
    private string _patchPath = string.Empty;
    private string _outputPath = string.Empty;
    private int _progressPct;
    private string _progressLabel = "대기 중...";    
    private string _progressPercent = string.Empty;
    private string _progressTime = "00:00 경과";
    private string _progressSpeed = string.Empty;

    public string InputPath
    {
        get => _inputPath;
        set { _inputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(InputHintVisibility)); }
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

    public Visibility InputHintVisibility => string.IsNullOrEmpty(InputPath) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PatchHintVisibility => string.IsNullOrEmpty(PatchPath) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutputHintVisibility => string.IsNullOrEmpty(OutputPath) ? Visibility.Visible : Visibility.Collapsed;

    public bool IsUnpackRunning => IsLocked && _currentMode == BuildMode.UnpackOnly;
    public bool IsRebuildRunning => IsLocked && _currentMode == BuildMode.RebuildOnly;
    public bool IsFullRunning => IsLocked && _currentMode == BuildMode.FullProcess;
    public bool UnpackEnabled => !IsLocked || _currentMode == BuildMode.UnpackOnly;
    public bool RebuildEnabled => !IsLocked || _currentMode == BuildMode.RebuildOnly;
    public bool StartEnabled => !IsLocked || _currentMode == BuildMode.FullProcess;

    public ICommand BrowseInputCommand { get; }
    public ICommand BrowsePatchCommand { get; }
    public ICommand BrowseOutputCommand { get; }

    public RepackMainViewModel()
    {
        OutputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        BrowseInputCommand = new RelayCommand(async _ => await BrowseInput());
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
        var keyStore = new KeyStore();
        string unpackedPath = Path.Combine(OutputPath, "unpacked");
        string outputCci = Utils.GetUniqueFilePath(Path.Combine(OutputPath, Path.GetFileNameWithoutExtension(InputPath) + "_Repack.cci"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long startTime = System.Diagnostics.Stopwatch.GetTimestamp();
        var progress = BuildProgressReporter();
        string inputFileName = Path.GetFileNameWithoutExtension(InputPath);
        var reporter = new ProgressReporter(inputFileName, string.Empty, 0, progress);
        bool isCompleted = false;

        try
        {
            if (!Directory.Exists(OutputPath))
                Directory.CreateDirectory(OutputPath);

            switch (mode)
            {
                case BuildMode.UnpackOnly:
                    await UnpackAsync(keyStore, unpackedPath, reporter.CreateAction(), ct);
                    break;
                case BuildMode.RebuildOnly:
                    await RepackAsync(keyStore, unpackedPath, outputCci, reporter.CreateAction(), ct);
                    break;
                case BuildMode.FullProcess:
                    await RepackDirectAsync(keyStore, outputCci, reporter.CreateAction(), ct);
                    break;
            }

            isCompleted = true;
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
            if (!isCompleted)
            {
                if (mode != BuildMode.UnpackOnly && File.Exists(outputCci))
                    try { File.Delete(outputCci); } catch { }

                if (mode == BuildMode.UnpackOnly && Directory.Exists(unpackedPath))
                    try { Directory.Delete(unpackedPath, true); } catch { }
            }
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

    private async Task UnpackAsync(KeyStore keyStore, string unpackedPath, Action<long, long>? reporter = null, CancellationToken ct = default)
    {
        Log("언팩 시작...", LogLevel.Highlight);

        await using var source = await OpenSourceAsync(InputPath, keyStore, (msg, level, _) => Log(msg, level),ct);

        foreach (var content in source.Contents)
        {
            int idx = content.ContentIndex;
            var (ncchStream, _) = await source.OpenContentDecrypted(idx);

            await using (ncchStream)
            {
                byte[] hdrBuf = new byte[NcchHeader.Size];
                await ncchStream.ReadExactlyAsync(hdrBuf, ct);
                var ncchHeader = NcchHeader.Parse(hdrBuf);
                ncchStream.Position = 0;

                var unpack = await NcchUnpacker.UnpackAsync(ncchStream, ncchHeader,  ct);
                string partDir = Path.Combine(unpackedPath, $"partition{idx}");

                await NcchUnpacker.SaveToDirectoryAsync(ncchStream, unpack, partDir, reporter, ct);
                Log($"파티션 {idx} 언팩 완료", LogLevel.Info);
            }
        }
    }

    private async Task RepackAsync(KeyStore keyStore, string unpackedPath, string outputCci, Action<long, long>? reporter = null, CancellationToken ct = default)
    {
        Log("리팩 시작...", LogLevel.Highlight);

        throw new NotImplementedException("폴더 기반 리팩은 추후 구현 예정");
    }

    private async Task RepackDirectAsync(KeyStore keyStore, string outputCci, Action<long, long>? reporter = null, CancellationToken ct = default)
    {
        Log("메모리 기반 리팩 시작...", LogLevel.Highlight);

        await using var source = await OpenSourceAsync(InputPath, keyStore, (msg, level, _) => Log(msg, level), ct);

        var repackedNcchs = new Dictionary<int, (NcchUnpackResult, byte[], Stream, RomFsUnpackResult?, IRomFsFileSource?)>();

        foreach (var content in source.Contents)
        {
            int idx = content.ContentIndex;
            var (ncchStream, _) = await source.OpenContentDecrypted(idx);

            byte[] hdrBuf = new byte[NcchHeader.Size];
            await ncchStream.ReadExactlyAsync(hdrBuf, ct);
            var ncchHeader = NcchHeader.Parse(hdrBuf);
            ncchStream.Position = 0;

            var unpack = await NcchUnpacker.UnpackAsync(ncchStream, ncchHeader,  ct);

            string? exefsPatchDir = GetPatchDir("exefs");
            string? romfsPatchDir = GetPatchDir("romfs");

            byte[] exefsBlock = unpack.ExeFs != null
                ? await ExeFsPacker.PackWithPatchAsync(unpack.ExeFs.Files, idx == 0 ? exefsPatchDir : null, ct)
                : [];

            IRomFsFileSource? patchSource = idx == 0 && romfsPatchDir != null
                ? new PatchFolderFileSource(romfsPatchDir)
                : null;

            repackedNcchs[idx] = (unpack, exefsBlock, ncchStream, unpack.RomFs, patchSource);
        }

        var repackedSource = await RepackedNcsdSource.CreateAsync(repackedNcchs, source.Contents, ct);

        await using var outputStream = File.Open(outputCci, FileMode.Create, FileAccess.ReadWrite);
        await NcsdBuilder.BuildAsync(repackedSource, outputStream, reporter, ct);

        Log($"출력: {outputCci}", LogLevel.Ok);
    }

    private string? GetPatchDir(string subFolder)
    {
        if (string.IsNullOrEmpty(PatchPath))
            return null;

        string path = Path.Combine(PatchPath, subFolder);
        return Directory.Exists(path) ? path : null;
    }

    private static async Task<INcsdSource> OpenSourceAsync(string inputPath, KeyStore keyStore, Action<string, LogLevel, string>? log = null, CancellationToken ct = default)
    {
        string ext = Path.GetExtension(inputPath).ToLowerInvariant();

        return ext switch
        {
            ".cia" => await new CiaReader(keyStore).OpenAsync(inputPath, log, ct),
            ".cci" or ".3ds" => await CciSource.OpenAsync(inputPath, keyStore, log, ct),
            _ => throw new NotSupportedException($"지원하지 않는 파일 형식: {ext}")
        };
    }

    private bool Validate(BuildMode mode, out string error)
    {
        error = string.Empty;

        if (mode != BuildMode.RebuildOnly && string.IsNullOrEmpty(InputPath))
        {
            error = "원본 파일을 선택하세요.";
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

    private void Log(string msg, LogLevel level = LogLevel.Info) =>
        Application.Current.Dispatcher.Invoke(() => LogEntries.Add(new LogEntry { Message = msg, Level = level }));

    private async Task BrowseInput()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "원본 파일 선택",
            Filter = "3DS ROM 파일|*.cci;*.3ds;*.cia"
        };
        if (dlg.ShowDialog() == true)
            InputPath = dlg.FileName;
    }

    private async Task BrowsePatch()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "패치 폴더 선택" };
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