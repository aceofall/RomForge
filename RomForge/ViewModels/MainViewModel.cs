using Common;
using Common.WPF.ViewModels;
using RomForge.Core;
using RomForge.Models;
using RomForge.ViewModels._3DS;
using RomForge.ViewModels.Util;
using System.Collections.ObjectModel;

namespace RomForge.ViewModels;

public class MainViewModel : ToolTabViewModel
{
    private int _selectedTabIndex;
    private readonly AppConfig _config = new AppConfig().Load();

    public PatchViewModel PatchVM { get; }
    public CompressViewModel CompressVM { get; }
    public _3DSMainViewModel Main3DsVM { get; }
    //public UtilMainViewModel UtilMainVM { get; }
    public SettingsViewModel Settings { get; }

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

    public ObservableCollection<LogEntry> ActiveLogEntries => _selectedTabIndex switch
    {
        0 => PatchVM.LogEntries,
        1 => CompressVM.LogEntries,
        3 => Main3DsVM.LogEntries,
        //3 => UtilMainVM.LogEntries,
        _ => PatchVM.LogEntries
    };

    public static string AppVersion => $"{AppDomain.CurrentDomain.FriendlyName} - Ver {Utils.ToAppVersionString()}";

    public MainViewModel()
    {
        PatchVM = new PatchViewModel(_config);
        CompressVM = new CompressViewModel(_config);
        Main3DsVM = new _3DSMainViewModel();
       // UtilMainVM = new UtilMainViewModel();
        Settings = new SettingsViewModel(_config);

        RegisterChild(PatchVM);
        RegisterChild(CompressVM);
        RegisterChild(Main3DsVM);
        //RegisterChild(UtilMainVM);
        RegisterChild(Settings);

        Main3DsVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Main3DsVM.LogEntries) && SelectedTabIndex == 2)
                OnPropertyChanged(nameof(ActiveLogEntries));
        };

        //UtilMainVM.PropertyChanged += (_, e) =>
        //{
        //    if (e.PropertyName == nameof(UtilMainVM.LogEntries) && SelectedTabIndex == 3)
        //        OnPropertyChanged(nameof(ActiveLogEntries));
        //};
    }

    public void SaveConfig() => _config.Save();
}