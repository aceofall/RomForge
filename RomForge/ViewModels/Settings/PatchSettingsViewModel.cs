using Common.WPF.ViewModels;
using RomForge.Core;

namespace RomForge.ViewModels.Settings;

public class PatchSettingsViewModel(AppConfig config) : ToolTabViewModel
{
    public bool UseCustomOutputFolder
    {
        get => config.Patch.OutputFolder != null;
        set
        {
            config.Patch.OutputFolder = value ? config.Patch.OutputFolder ?? string.Empty : null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFolder));
        }
    }

    public string OutputFolder
    {
        get => config.Patch.OutputFolder ?? string.Empty;
        set
        {
            config.Patch.OutputFolder = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }
}