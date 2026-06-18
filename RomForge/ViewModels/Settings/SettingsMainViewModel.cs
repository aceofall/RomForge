using Common.WPF.ViewModels;
using RomForge.Core;

namespace RomForge.ViewModels.Settings;

public class SettingsMainViewModel(AppConfig config) : ToolTabViewModel
{
    public PatchSettingsMainViewModel Patch { get; } = new PatchSettingsMainViewModel(config);

    public CompressSettingsMainViewModel Compress { get; } = new CompressSettingsMainViewModel(config);
}