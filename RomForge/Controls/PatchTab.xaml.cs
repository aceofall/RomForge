using Microsoft.Win32;
using RomForge.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomForge.Controls;

public partial class PatchTab : UserControl
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public PatchTab()
    {
        InitializeComponent();
    }

    private static string? OpenSingleFileDialog(string title)
    {
        var dlg = new OpenFileDialog { Title = title };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void NormalSourceDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var path = OpenSingleFileDialog("원본 파일 선택");
        if (path != null)
            ViewModel.PatchVM.NormalVM.SourcePath = path;
    }

    private void NormalSourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.NormalVM.SourcePath = files[0];
    }

    private void NormalPatchDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "패치 파일 선택" };
        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.NormalVM.PatchPath = dlg.FileName;
    }

    private void NormalPatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.NormalVM.PatchPath = files[0];
    }

    private void ArcadeSourceDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var path = OpenSingleFileDialog("원본 ZIP 선택");
        if (path != null)
            ViewModel.PatchVM.ArcadeVM.SourcePath = path;
    }

    private void ArcadeSourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.ArcadeVM.SourcePath = files[0];
    }

    private void ArcadePatchDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "패치 파일 선택" };
        if (dlg.ShowDialog() == true)
        {
            ViewModel.PatchVM.ArcadeVM.PatchPath = dlg.FileName;
            return;
        }

        var folderDlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "패치 폴더 선택",
            UseDescriptionForTitle = true
        };
        if (folderDlg.ShowDialog() == true)
            ViewModel.PatchVM.ArcadeVM.PatchPath = folderDlg.SelectedPath;
    }

    private void ArcadePatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        ViewModel.PatchVM.ArcadeVM.PatchPath = files[0];
    }

    private void MatchCard_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MatchCard_PatchDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.Tag is not ArcadeMatchItem item) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        ViewModel.PatchVM.ArcadeVM.ManualMatch(item, files[0]);
    }
}
