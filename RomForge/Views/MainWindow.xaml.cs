using NSW.WPF.UI;
using Patch.Core.Formats.DCP.Services;
using RomForge.Core.UI.Helpers;
using RomForge.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
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
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await DcpGdRomApplier.ApplyAsync(
                gdiPath: @"\\CDH5\download\게임\한글패치\GDI\Puyo Puyo 4\Puyo Puyo 4 (Japan).gdi",
                //gdiPath: @"\\CDH5\download\게임\한글패치\GDI\Rez\Rez v1.003 (2001)(Sega)(PAL)(M6)[!].gdi",
                dcpPath: @"\\CDH5\download\게임\한글패치\GDI\Puyo Puyo 4\Puyo Puyo 4 (Dreamcast) KR v1.0.0.dcp",
                outputDir: @"D:\Output");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}", "GDI 추출 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        int value = 1;

        _ = Win32API.DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int));
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.LogBoxHeight = LogRow.Height.Value;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        ViewModel.SaveConfig();

        bool busy = ViewModel.IsAnyChildLocked();

        if (!busy)
            return;

        var result = MessageBoxHelper.ShowQuestion("작업이 진행 중입니다. 취소하고 종료할까요?");

        if (result)
            ViewModel.CancelAll();
        else
            e.Cancel = true;
    }
}