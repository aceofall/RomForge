using RomForge.Core;

namespace RomForge.ViewModels.Settings;

public class SettingsMainViewModel : MultiToolTabViewModel
{
    public PatchSettingsMainViewModel Patch { get; }

    public CompressSettingsMainViewModel Compress { get; }

    public PS1SettingsMainViewModel PS1 { get; }

    public SettingsMainViewModel(AppConfig config)
    {
        Patch = new PatchSettingsMainViewModel(config);
        Compress = new CompressSettingsMainViewModel(config);
        PS1 = new PS1SettingsMainViewModel(config);

        Tools.Add(Patch);
        Tools.Add(Compress);
        Tools.Add(PS1);

        InitializeMultiTools();
    }
}