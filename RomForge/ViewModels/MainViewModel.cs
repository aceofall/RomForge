using Common;
using Patch.Core;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RomForge.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private int _selectedTabIndex;

    private readonly AppConfig _config = new AppConfig().Load();

    public PatchViewModel PatchVM { get; }
    public CompressViewModel CompressVM { get; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveLogEntries)); }
    }

    public System.Collections.ObjectModel.ObservableCollection<LogEntry> ActiveLogEntries =>
        _selectedTabIndex == 0 ? PatchVM.LogEntries : CompressVM.LogEntries;

    #region 설정 프로퍼티 (SettingsWindow 바인딩용)

    public bool OutputModeNormal
    {
        get => _config.Patch.OutputMode == OutputMode.Normal;
        set
        {
            if (value) _config.Patch.OutputMode = OutputMode.Normal;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputModeArcade));
        }
    }

    public bool OutputModeArcade
    {
        get => _config.Patch.OutputMode == OutputMode.Arcade;
        set
        {
            if (value) _config.Patch.OutputMode = OutputMode.Arcade;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputModeNormal));
        }
    }

    public bool UseCustomOutputFolder
    {
        get => _config.Patch.OutputFolder != null;
        set
        {
            _config.Patch.OutputFolder = value ? _config.Patch.OutputFolder ?? string.Empty : null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFolder));
        }
    }

    public string OutputFolder
    {
        get => _config.Patch.OutputFolder ?? string.Empty;
        set { _config.Patch.OutputFolder = string.IsNullOrWhiteSpace(value) ? null : value; OnPropertyChanged(); }
    }

    #endregion

    public static string AppVersion =>
        $"{AppDomain.CurrentDomain.FriendlyName} - Ver {GetVersion()}";

    public MainViewModel()
    {
        PatchVM = new PatchViewModel(_config);
        CompressVM = new CompressViewModel(_config);
    }

    public void SaveConfig() => _config.Save();

    private static string GetVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return asm is null ? "1.0.0" : $"{asm.Major}.{asm.Minor}.{asm.Build}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}