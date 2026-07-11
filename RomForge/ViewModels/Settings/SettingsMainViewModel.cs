namespace RomForge.ViewModels.Settings;

public class SettingsMainViewModel : MultiToolTabViewModel
{
    public PatchSettingsMainViewModel Patch { get; } = new();

    public CompressSettingsMainViewModel Compress { get; } = new();

    public PSSettingsMainViewModel PS1 { get; } = new();

    public SettingsMainViewModel()
    {
        Tools.Add(Patch);
        Tools.Add(Compress);
        Tools.Add(PS1);

        InitializeMultiTools();
    }
}