using Microsoft.Win32;
using PickPack.Disk;
using RomForge.Core.Services;
using RomForge.ViewModels._3DS;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace RomForge.Controls._3DS;

public partial class InstallMainTab : UserControl
{
    private InstallerMainViewModel ViewModel => (InstallerMainViewModel)DataContext;
    private UsbDetector _usbDetector = null!;
    private bool _suppressSelectionChanged;

    public InstallMainTab()
    {
        InitializeComponent();

        Loaded += MainTab_Loaded;
    }

    private void MainTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_usbDetector != null) 
            return;

        if (ViewModel == null) 
            return;

        _ = InitializeDrivesAsync();

        Window parentWindow = Window.GetWindow(this);

        if (parentWindow != null)
        {
            _usbDetector = new UsbDetector();

            _usbDetector.Register(parentWindow);

            _usbDetector.DeviceChanged += async () =>
            {
                await Task.Delay(500);
                await Dispatcher.InvokeAsync(InitializeDrivesAsync);
            };
        }
    }

    private async Task InitializeDrivesAsync()
    {
        if (ViewModel == null) 
            return;

        _suppressSelectionChanged = true;
        SdDriveComboBox.Items.Clear();

        var infos = await Task.Run(() => DriveInfos.GetDriveInfos());

        foreach (var info in infos)
            SdDriveComboBox.Items.Add(info);

        if (SdDriveComboBox.Items.Count == 0)
        {
            ViewModel.SdPath = string.Empty;
            MovablePathBox.Text = string.Empty;
            SdDriveComboBox.IsEnabled = false;
            _suppressSelectionChanged = false;
            return;
        }

        SdDriveComboBox.IsEnabled = true;

        if (SdDriveComboBox.Items[^1] is DriveInfos lastDrive)
        {
            ViewModel.SdPath = lastDrive.DriveLetter!;
            await ViewModel.CheckAndSetMovablePathAsync(ViewModel.SdPath!);
            MovablePathBox.Text = ViewModel.MovablePath;
        }

        _suppressSelectionChanged = false;
        SdDriveComboBox.SelectedIndex = SdDriveComboBox.Items.Count - 1;
    }

    private async void SdDriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged || ViewModel == null) 
            return;

        if (SdDriveComboBox.SelectedItem is not DriveInfos selectedItem) 
            return;

        ViewModel.SdPath = selectedItem.DriveLetter!;

        if (string.IsNullOrEmpty(ViewModel.SdPath)) 
            return;

        await ViewModel.CheckAndSetMovablePathAsync(ViewModel.SdPath);
        MovablePathBox.Text = ViewModel.MovablePath;
    }

    private void BrowseMovable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "movable.sed 선택",
            Filter = "movable.sed|movable.sed|모든 파일|*.*",
            FileName = "movable.sed",
        };

        if (dialog.ShowDialog() != true) 
            return;

        ViewModel.MovablePath = dialog.FileName;
        MovablePathBox.Text = ViewModel.MovablePath;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.CancelExtract();

    private void Image_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://seedminer.hacks.guide/",
            UseShellExecute = true
        });
    }

    private void MovableTextBlock_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://seedminer.hacks.guide/",
            UseShellExecute = true
        });
    }
}