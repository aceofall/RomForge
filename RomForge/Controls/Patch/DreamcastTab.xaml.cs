using Microsoft.Win32;
using RomForge.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomForge.Controls.Patch;

public partial class DreamcastTab : UserControl
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private const string GdiExtension = ".gdi";
    private const string DcpExtension = ".dcp";

    public DreamcastTab()
    {
        InitializeComponent();
    }

    private void DreamcastSourceDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "원본 GDI 선택",
            Filter = "GDI 파일|*.gdi|모든 파일|*.*"
        };

        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.DreamcastVM.SourcePath = dlg.FileName;
    }

    private void DreamcastSourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var gdiFiles = files.Where(f => Path.GetExtension(f).Equals(GdiExtension, StringComparison.OrdinalIgnoreCase)).ToList();
        var dcpFiles = files.Where(f => Path.GetExtension(f).Equals(DcpExtension, StringComparison.OrdinalIgnoreCase)).ToList();

        if (dcpFiles.Count > 0)
            ViewModel.PatchVM.DreamcastVM.PatchPath = dcpFiles[0];

        if (gdiFiles.Count > 0)
            ViewModel.PatchVM.DreamcastVM.SourcePath = gdiFiles[0];
    }

    private void DreamcastPatchDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "DCP 패치 선택",
            Filter = "DCP 파일|*.dcp|모든 파일|*.*"
        };

        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.DreamcastVM.PatchPath = dlg.FileName;
    }

    private void DreamcastPatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var gdiFiles = files.Where(f => Path.GetExtension(f).Equals(GdiExtension, StringComparison.OrdinalIgnoreCase)).ToList();
        var dcpFiles = files.Where(f => Path.GetExtension(f).Equals(DcpExtension, StringComparison.OrdinalIgnoreCase)).ToList();

        if (dcpFiles.Count > 0)
            ViewModel.PatchVM.DreamcastVM.PatchPath = dcpFiles[0];

        if (gdiFiles.Count > 0)
            ViewModel.PatchVM.DreamcastVM.SourcePath = gdiFiles[0];
    }
}