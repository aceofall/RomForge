using Common;
using Common.WPF.ViewModels;
using RomForge.Core;
using RomForge.Models;
using System.Collections.ObjectModel;

namespace RomForge.ViewModels;

public class MainViewModel : ToolTabViewModel
{
    private int _selectedTabIndex;

    private readonly AppConfig _config = new AppConfig().Load();

    public PatchViewModel PatchVM { get; }

    public CompressViewModel CompressVM { get; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            _selectedTabIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveLogEntries));
        }
    }

    public ObservableCollection<LogEntry> ActiveLogEntries => _selectedTabIndex == 0 ? PatchVM.LogEntries : CompressVM.LogEntries;

    public SettingsViewModel Settings { get; }


    public static string AppVersion => $"{AppDomain.CurrentDomain.FriendlyName} - Ver {Utils.ToAppVersionString()}";

    public MainViewModel()
    {
        PatchVM = new PatchViewModel(_config);
        CompressVM = new CompressViewModel(_config);
        Settings = new SettingsViewModel(_config);

        RegisterChild(PatchVM);
        RegisterChild(CompressVM);
        RegisterChild(Settings);
    }

    public void SaveConfig() => _config.Save();
}