using RomForge.Core;

namespace RomForge.ViewModels.PS1;

public class PS1MainViewModel : MultiToolTabViewModel
{
    public PackingMainViewModel PackingVM { get; }

    public UnpackingMainViewModel UnPackingVM { get; }

    public event EventHandler RunNavigatePackingSettings;

    public PS1MainViewModel(AppConfig config)
    {
        PackingVM = new PackingMainViewModel(config);
        PackingVM.RunNavigateSettings += (sender, e) => RunNavigatePackingSettings?.Invoke(sender, e);
        UnPackingVM = new UnpackingMainViewModel();

        Tools.Add(PackingVM);
        Tools.Add(UnPackingVM);

        InitializeMultiTools();
    }
}