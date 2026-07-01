using RomForge.Core;

namespace RomForge.ViewModels.Switch;

public class SwitchMainViewModel : MultiToolTabViewModel
{
    public RepackMainViewModel RepackVM { get; } = new();

    public MergeMainViewModel MergeVM { get; }

    public ConverterMainViewModel ConverterVM { get; }

    public KeygenMainViewModel KeygenVM { get; } = new();

    public SwitchMainViewModel(AppConfig config)
    {
        MergeVM = new MergeMainViewModel(config);
        ConverterVM = new ConverterMainViewModel(config);

        Tools.Add(RepackVM);
        Tools.Add(MergeVM);
        Tools.Add(ConverterVM);
        Tools.Add(KeygenVM);

        InitializeMultiTools();
    }
}