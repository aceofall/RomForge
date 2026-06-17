using RomForge.ViewModels;
using System.Windows.Controls;

namespace RomForge.Controls;

public partial class SettingsTab : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public SettingsTab()
    {
        InitializeComponent();
    }
}