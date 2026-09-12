using RomForge.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace RomForge.Controls.Patch;

public partial class VitaTab : UserControl
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public VitaTab()
    {
        InitializeComponent();
    }

    private void SourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.VitaVM.SourcePath = files[0];
    }

    private void PatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.VitaVM.PatchPath = files[0];
    }

    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "출력 폴더 선택",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.VitaVM.OutputPath = dlg.SelectedPath;
    }
}