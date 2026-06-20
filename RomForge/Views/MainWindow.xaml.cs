using NSW.WPF.UI;
using PBP.Core.Models;
using PBP.Core.Services;
using RomForge.Helpers;
using RomForge.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace RomForge.Views;

public partial class MainWindow : Window
{

    private MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        Closing += MainWindow_Closing;

        //PbpPackager.WriteMultiDiscAsync(
        //    outputPath: @"D:\download\발키리 프로파일\output\test_multi.pbp",
        //    discs:
        //    [
        //        (DiskSource.FromBinCue(@"D:\download\발키리 프로파일\output\Valkyrie Profile (Japan) (Disc 1).bin", @"D:\download\발키리 프로파일\output\Valkyrie Profile (Japan) (Disc 1).cue"), "Valkyrie Profile - Disc 1"),
        //        (DiskSource.FromBinCue(@"D:\download\발키리 프로파일\output\Valkyrie Profile (Japan) (Disc 2) (v1.1).bin", @"D:\download\발키리 프로파일\output\Valkyrie Profile (Japan) (Disc 2) (v1.1).cue"), "Valkyrie Profile - Disc 2"),
        //    ],

        //    mainGameTitle: "Valkyrie Profile");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        int value = 1;

        _ = Win32API.DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        ViewModel.SaveConfig();
        bool busy = ViewModel.CompressVM.IsLocked || ViewModel.PatchVM.IsLocked;

        if (!busy)
            return;

        var result = MessageBoxHelper.ShowQuestion("작업이 진행 중입니다. 취소하고 종료할까요?");

        if (result)
        {
            ViewModel.CompressVM.CancelCommand.Execute(null);
            ViewModel.PatchVM.CancelCommand.Execute(null);
        }
        else
            e.Cancel = true;
    }
}