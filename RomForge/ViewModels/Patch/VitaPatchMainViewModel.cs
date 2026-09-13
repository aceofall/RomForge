using Common;
using Common.WPF.ViewModels;
using NSW.WPF.Services;
using RomForge.Core.Models;
using RomForge.Core.Services.Patch;
using RomForge.Core.UI.Command;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Vita.Core.Models;
using Vita.Core.Services;

namespace RomForge.ViewModels.Patch;

public class VitaPatchMainViewModel : ToolTabViewModel, IPatchViewModel
{
    private readonly Stopwatch _totalSw = new();
    private CancellationTokenSource? _cts;

    private bool _isPkgMode = true;
    private string? _sourcePath = string.Empty;
    private string? _patchPath = string.Empty;
    private string? _outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
    private bool _buildEmu = true;
    private bool _buildRetail;
    private bool _mergePatchIntoGame = false;
    private int _progressPct;
    private string _progressLabel = string.Empty;
    private bool _isSyncing;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public ObservableCollection<VitaPkgRowViewModel> PkgRows { get; } = [];

    public bool IsPkgMode
    {
        get => _isPkgMode;
        set { _isPkgMode = value; OnPropertyChanged(); }
    }

    public string? SourcePath
    {
        get => _sourcePath;
        set
        {
            if (_sourcePath != value)
            {
                _sourcePath = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PatchPath
    {
        get => _patchPath;
        set
        {
            if (_patchPath != value)
            {
                _patchPath = value;
                OnPropertyChanged();
            }
        }
    }

    public string? OutputPath
    {
        get => _outputPath;
        set { _outputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputHintVisibility)); }
    }

    public Visibility OutputHintVisibility => string.IsNullOrWhiteSpace(OutputPath) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EntriesHintVisibility => PkgRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool BuildEmu
    {
        get => _buildEmu;
        set { _buildEmu = value; OnPropertyChanged(); }
    }

    public bool BuildRetail
    {
        get => _buildRetail;
        set { _buildRetail = value; OnPropertyChanged(); }
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

    public bool MergePatchIntoGame
    {
        get => _mergePatchIntoGame;
        set { _mergePatchIntoGame = value; OnPropertyChanged(); }
    }

    public ICommand RunCommand { get; }
    public ICommand RemoveRowCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand RemoveAllCommand { get; }
    public ICommand BrowseOutputCommand { get; }

    private VitaPkgRowViewModel? _selectedRow;
    public VitaPkgRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set { _selectedRow = value; OnPropertyChanged(); }
    }

    public VitaPatchMainViewModel()
    {
        RunCommand = new RelayCommand(async _ => await RunAsync(), _ => !IsLocked && CanRun());
        RemoveRowCommand = new RelayCommand(o => RemovePkgRow(o as VitaPkgRowViewModel ?? SelectedRow));
        RemoveSelectedCommand = new RelayCommand(_ => { if (SelectedRow != null) RemovePkgRow(SelectedRow); }, _ => SelectedRow != null);
        RemoveAllCommand = new RelayCommand(_ => PkgRows.Clear(), _ => PkgRows.Count > 0);
        BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
        CancelCommand = new RelayCommand(_ => Cancel());

        PkgRows.CollectionChanged += PkgRows_CollectionChanged;
    }

    private void PkgRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (VitaPkgRowViewModel row in e.NewItems)
                row.PropertyChanged += Row_PropertyChanged;
        }

        if (e.OldItems != null)
        {
            foreach (VitaPkgRowViewModel row in e.OldItems)
                row.PropertyChanged -= Row_PropertyChanged;
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSyncing) 
            return;

        if (sender is not VitaPkgRowViewModel changedRow)
            return;

        _isSyncing = true;
        try
        {
            if (e.PropertyName == nameof(VitaPkgRowViewModel.PatchPath))
            {
                foreach (var row in PkgRows)
                {
                    if (row != changedRow && !string.Equals(row.PatchPath, changedRow.PatchPath))
                        row.PatchPath = changedRow.PatchPath;
                }
            }
            else if (e.PropertyName == nameof(VitaPkgRowViewModel.License))
            {
                if (changedRow.Category != VitaContentCategory.Addcont)
                {
                    foreach (var row in PkgRows)
                    {
                        if (row.Category != VitaContentCategory.Addcont && row != changedRow)
                        {
                            if (!string.Equals(row.License, changedRow.License))
                                row.License = changedRow.License;
                        }
                    }
                }
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void BrowseOutput()
    {
        var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "출력 폴더 선택",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == true)
            OutputPath = dlg.SelectedPath;
    }

    private bool CanRun()
    {
        if (string.IsNullOrWhiteSpace(OutputPath) || (!BuildEmu && !BuildRetail))
            return false;

        if (IsPkgMode)
            return PkgRows.Count > 0;

        return !string.IsNullOrWhiteSpace(SourcePath);
    }

    public void AddPkgFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) 
            return;

        bool isDirectory = Directory.Exists(path);
        bool isPkg = path.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase);
        bool isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (!isDirectory && !isPkg && !isZip)
            return;

        if (PkgRows.Any(r => string.Equals(r.PkgPath, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var row = new VitaPkgRowViewModel(path);

        row.Probe();

        string? dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir) && isPkg)
        {
            string txtPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".txt");

            if (File.Exists(txtPath))
            {
                try
                {
                    string txtContent = File.ReadAllText(txtPath).Trim();

                    if (!string.IsNullOrEmpty(txtContent))
                        row.License = txtContent;
                }
                catch
                {
                }
            }
        }

        var existingAny = PkgRows.FirstOrDefault();

        if (existingAny != null && !string.IsNullOrEmpty(existingAny.PatchPath))
            row.PatchPath = existingAny.PatchPath;

        if (row.Category != VitaContentCategory.Addcont)
        {
            var existingAppOrPatch = PkgRows.FirstOrDefault(r => r.Category != VitaContentCategory.Addcont && !string.IsNullOrEmpty(r.License));

            if (existingAppOrPatch != null && string.IsNullOrEmpty(row.License))
                row.License = existingAppOrPatch.License;
        }

        PkgRows.Add(row);
        OnPropertyChanged(nameof(EntriesHintVisibility));
    }

    public void RemovePkgRow(VitaPkgRowViewModel row)
    {
        if (row != null)
        {
            row.PropertyChanged -= Row_PropertyChanged;

            PkgRows.Remove(row);
            OnPropertyChanged(nameof(EntriesHintVisibility));
        }
    }

    public async Task RunAsync()
    {
        _totalSw.Restart();

        using (BeginWork())
        {
            try
            {
                _cts = new CancellationTokenSource();

                if (IsPkgMode)
                {
                    var missingApp = PkgRows
                        .GroupBy(r => r.TitleId, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.All(r => r.Category != VitaContentCategory.App) && g.Any(r => r.Category is VitaContentCategory.Patch or VitaContentCategory.Addcont))
                        .Select(g => g.Key);

                    foreach (var titleId in missingApp)
                        Log($"{titleId}: app 없이 patch/dlc만 존재함 - 결과물이 불완전할 수 있습니다.", LogLevel.Error);
                }

                string baseName = IsPkgMode
                    ? (PkgRows.FirstOrDefault(r => r.Category == VitaContentCategory.App)?.TitleId ?? PkgRows.FirstOrDefault()?.TitleId ?? "vita")
                    : Path.GetFileNameWithoutExtension(SourcePath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                var progress = new Progress<double>(p => ProgressPct = (int)(p * 100));

                string currentPatchPath = IsPkgMode ? (PkgRows.FirstOrDefault()?.PatchPath ?? PatchPath ?? string.Empty) : (PatchPath ?? string.Empty);

                if (BuildEmu)
                {
                    Log("에뮬용 패치 생성 중...");

                    string emuFileName = PatchVersionInfoExtractor.ApplySuffix($"{baseName}_emu.zip", currentPatchPath);
                    string emuZip = Utils.GetUniqueFilePath(Path.Combine(OutputPath!, emuFileName));
                    var result = IsPkgMode
                        ? await VitaPatchOnlyBuilder.BuildFromPkgBatchAsync([.. PkgRows.Select(r => r.ToBatchEntry())], currentPatchPath, emuZip, VitaOutputTarget.Emu, msg => { Log(msg); ProgressLabel = msg; }, progress, _cts.Token)
                        : await VitaPatchOnlyBuilder.BuildAsync(SourcePath!, currentPatchPath, emuZip, VitaOutputTarget.Emu, msg => { Log(msg); ProgressLabel = msg; }, progress, _cts.Token);

                    Log($"에뮬용 완료: 매칭 {result.MatchedCandidates}개 중 {result.PatchedSuccessfully}개 성공 -> {emuZip}", LogLevel.Ok);
                }

                if (BuildRetail)
                {
                    Log("실기용 패치 생성 중...");

                    string retailFileName = PatchVersionInfoExtractor.ApplySuffix($"{baseName}_retail.zip", currentPatchPath);
                    string retailZip = Utils.GetUniqueFilePath(Path.Combine(OutputPath!, retailFileName));
                    var result = IsPkgMode
                        ? await VitaPatchOnlyBuilder.BuildFromPkgBatchAsync([.. PkgRows.Select(r => r.ToBatchEntry())], currentPatchPath, retailZip, VitaOutputTarget.Retail, msg => { Log(msg); ProgressLabel = msg; }, progress, _cts.Token)
                        : await VitaPatchOnlyBuilder.BuildAsync(SourcePath!, currentPatchPath, retailZip, VitaOutputTarget.Retail, msg => { Log(msg); ProgressLabel = msg; }, progress, _cts.Token);

                    Log($"실기용 완료: 매칭 {result.MatchedCandidates}개 중 {result.PatchedSuccessfully}개 성공 -> {retailZip}", LogLevel.Ok);
                }

                Log($"전체 완료 ({_totalSw.Elapsed:mm\\:ss})", LogLevel.Ok);

                OutputPath?.OpenFolder();
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
                _cts?.Dispose();
                _cts = null;
                ProgressPct = 0;
                ProgressLabel = string.Empty;
            }
        }
    }

    public void Clear()
    {
        _cts?.Cancel();

        SourcePath = null;
        PatchPath = null;
        PkgRows.Clear();
        OnPropertyChanged(nameof(EntriesHintVisibility));

        ProgressPct = 0;
        ProgressLabel = string.Empty;

        LogEntries.Clear();
    }

    public void Cancel() => _cts?.Cancel();

    public void Log(string msg, LogLevel level = LogLevel.Info) => Application.Current.Dispatcher.Invoke(() => LogEntries.Add(new LogEntry { Message = msg, Level = level }));
}