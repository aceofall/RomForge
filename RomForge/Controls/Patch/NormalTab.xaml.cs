using Microsoft.Win32;
using RomForge.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomForge.Controls.Patch;

public partial class NormalTab : UserControl
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private static class PatchExtensions
    {
        public static readonly string[] AllowedExtensions = [".ips", ".bps", ".ups", ".ppf", ".aps", ".xdelta"];
        public static string FileFilter => $"패치 파일|{string.Join(";", AllowedExtensions.Select(ext => "*" + ext))}|모든 파일|*.*";
    }

    public NormalTab()
    {
        InitializeComponent();
    }

    private void NormalSourceDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "원본 파일 선택" };

        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.NormalVM.SourcePath = dlg.FileName;
    }

    private void NormalSourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var patchFiles = files
                .Where(f => PatchExtensions.AllowedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            var sourceFiles = files.Except(patchFiles).ToList();

            if (patchFiles.Count > 0)
                ViewModel.PatchVM.NormalVM.PatchPath = patchFiles[0];

            if (sourceFiles.Count > 0)
                ViewModel.PatchVM.NormalVM.SourcePath = sourceFiles[0];
        }
    }

    private void NormalPatchDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "패치 파일 선택",
            Filter = PatchExtensions.FileFilter
        };

        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.NormalVM.PatchPath = dlg.FileName;
    }

    private void NormalPatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var patchFiles = files
                .Where(f => PatchExtensions.AllowedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            var sourceFiles = files.Except(patchFiles).ToList();

            if (patchFiles.Count > 0)
                ViewModel.PatchVM.NormalVM.PatchPath = patchFiles[0];

            if (sourceFiles.Count > 0)
                ViewModel.PatchVM.NormalVM.SourcePath = sourceFiles[0];
        }
    }
}