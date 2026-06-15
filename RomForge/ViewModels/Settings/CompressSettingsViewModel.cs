using Common.WPF.ViewModels;
using RomForge.Core;

namespace RomForge.ViewModels.Settings;

public class CompressSettingsViewModel(AppConfig config) : ToolTabViewModel
{
    public double SwitchCompressLevel
    {
        get => config.Switch.CompressLevel;
        set { config.Switch.CompressLevel = (int)value; OnPropertyChanged(); }
    }

    public bool SwitchIsValidationEnabled
    {
        get => config.Switch.VerifyCompress;
        set { config.Switch.VerifyCompress = value; OnPropertyChanged(); }
    }

    public bool SwitchUseBlockMode
    {
        get => config.Switch.UseBlockMode;
        set
        {
            config.Switch.UseBlockMode = value;
            if (value) config.Switch.UseBlocklessMode = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SwitchUseBlocklessMode));
        }
    }

    public bool SwitchUseBlocklessMode
    {
        get => config.Switch.UseBlocklessMode;
        set
        {
            config.Switch.UseBlocklessMode = value;
            if (value) config.Switch.UseBlockMode = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SwitchUseBlockMode));
        }
    }

    public double AzaharCompressLevel
    {
        get => config.Azahar.CompressLevel;
        set { config.Azahar.CompressLevel = (int)value; OnPropertyChanged(); }
    }

    public double DolphinCompressLevel
    {
        get => config.Dolphin.CompressLevel;
        set { config.Dolphin.CompressLevel = (int)value; OnPropertyChanged(); }
    }
}