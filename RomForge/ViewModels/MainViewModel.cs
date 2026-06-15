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

    #region 압축 설정

    public double SwitchCompressLevel
    {
        get => _config.Switch.CompressLevel;
        set { _config.Switch.CompressLevel = (int)value; OnPropertyChanged(); }
    }

    public bool SwitchIsValidationEnabled
    {
        get => _config.Switch.VerifyCompress;
        set { _config.Switch.VerifyCompress = value; OnPropertyChanged(); }
    }

    public bool SwitchUseBlockMode
    {
        get => _config.Switch.UseBlockMode;
        set
        {
            _config.Switch.UseBlockMode = value;

            if (value) 
                _config.Switch.UseBlocklessMode = false;

            OnPropertyChanged();
            OnPropertyChanged(nameof(SwitchUseBlocklessMode));
        }
    }

    public bool SwitchUseBlocklessMode
    {
        get => _config.Switch.UseBlocklessMode;
        set
        {
            _config.Switch.UseBlocklessMode = value;

            if (value) 
                _config.Switch.UseBlockMode = false;

            OnPropertyChanged();
            OnPropertyChanged(nameof(SwitchUseBlockMode));
        }
    }

    public double AzaharCompressLevel
    {
        get => _config.Azahar.CompressLevel;
        set { _config.Azahar.CompressLevel = (int)value; OnPropertyChanged(); }
    }

    public double DolphinCompressLevel
    {
        get => _config.Dolphin.CompressLevel;
        set { _config.Dolphin.CompressLevel = (int)value; OnPropertyChanged(); }
    }

    #endregion

    #region 패치 설정

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
        set
        {
            _config.Patch.OutputFolder = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    #endregion

    public static string AppVersion => $"{AppDomain.CurrentDomain.FriendlyName} - Ver {Utils.ToAppVersionString()}";

    public MainViewModel()
    {
        PatchVM = new PatchViewModel(_config);
        CompressVM = new CompressViewModel(_config);

        RegisterChild(PatchVM);
        RegisterChild(CompressVM);
    }

    public void SaveConfig() => _config.Save();
}