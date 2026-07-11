namespace RomForge.ViewModels.WiiU;

public class WiiUMainViewModel: MultiToolTabViewModel
{
    public RepackMainViewModel RepackVM { get; } = new();

    public WiiUMainViewModel()
    {
        Tools.Add(RepackVM);

        InitializeMultiTools();
    }
}