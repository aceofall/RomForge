using NSW.WPF.UI;
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

        //var path = @"D:\download\발키리 프로파일\output\Valkyrie Profile (Jap2Kor_v0.9.1).pbp";

        //using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        //var header = new uint[10];
        //var buf = new byte[4];

        //for (var i = 0; i < 10; i++)
        //{
        //    fs.Read(buf, 0, 4);
        //    header[i] = BitConverter.ToUInt32(buf, 0);
        //}

        //string[] names = { "Magic", "Version", "SFO", "ICON0", "ICON1", "PIC0", "PIC1", "SND0", "DATA.PSP", "DATA.PSAR" };

        //for (var i = 0; i < 10; i++)
        //{
        //    Debug.WriteLine($"header[{i}] {names[i],-10} = 0x{header[i]:X8} ({header[i]})");
        //}

        //Debug.WriteLine($"SFO size       = {header[3] - header[2]}");
        //Debug.WriteLine($"ICON0 size     = {header[4] - header[3]}");
        //Debug.WriteLine($"ICON1 size     = {header[5] - header[4]}");
        //Debug.WriteLine($"PIC0 size      = {header[6] - header[5]}");
        //Debug.WriteLine($"PIC1 size      = {header[7] - header[6]}");
        //Debug.WriteLine($"SND0 size      = {header[8] - header[7]}");
        //Debug.WriteLine($"DATA.PSP size  = {header[9] - header[8]}");
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