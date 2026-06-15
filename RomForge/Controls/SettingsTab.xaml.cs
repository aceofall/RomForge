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

    private void BtnBrowseFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Vm == null) 
            return;

        var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "출력 폴더 선택",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == true)
            Vm.Settings.Patch.OutputFolder = dlg.SelectedPath;
    }
}