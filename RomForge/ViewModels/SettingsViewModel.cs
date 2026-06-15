using Common.WPF.ViewModels;
using RomForge.Core;
using RomForge.ViewModels.Settings;

namespace RomForge.ViewModels;

public class SettingsViewModel(AppConfig config) : ToolTabViewModel
{
    public PatchSettingsViewModel Patch { get; } = new PatchSettingsViewModel(config);

    public CompressSettingsViewModel Compress { get; } = new CompressSettingsViewModel(config);
}