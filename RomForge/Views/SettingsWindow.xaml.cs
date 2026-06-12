using RomForge.Helpers;
using RomForge.ViewModels;
using System.Windows;
using System.Windows.Interop;

namespace RomForge.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        int value = 1;
        Win32API.DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int));
    }

    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.SaveConfig();
    }

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "출력 폴더 선택",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == true)
            _vm.OutputFolder = dlg.SelectedPath;
    }
}