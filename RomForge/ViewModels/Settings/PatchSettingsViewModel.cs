using Common.WPF.ViewModels;
using RomForge.Core;

namespace RomForge.ViewModels.Settings;

public class PatchSettingsViewModel : ToolTabViewModel
{
    private readonly AppConfig _config;

    public PatchSettingsViewModel(AppConfig config)
    {
        _config = config;
        _config.Patch.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PatchConfig.AutoCompress))
                OnPropertyChanged(nameof(AutoCompress));
        };
    }

    public bool AutoCompress
    {
        get => _config.Patch.AutoCompress;
        set
        {
            _config.Patch.AutoCompress = value;
            OnPropertyChanged();
        }
    }
}