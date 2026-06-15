using Microsoft.Win32;
using RomForge.Core.Services;
using RomForge.ViewModels._3DS;
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

        this.Loaded += MainTab_Loaded;
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
            MessageBox.Show("연결된 이동식 드라이브가 없습니다.\n연결 후 재시도 하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            SdDriveComboBox.IsEnabled = false;
            _suppressSelectionChanged = false;
            return;
        }

        SdDriveComboBox.IsEnabled = true;

        if (SdDriveComboBox.Items[0] is DriveInfos firstDrive)
        {
            ViewModel.SdPath = firstDrive.DriveLetter!;
            await ViewModel.CheckAndSetMovablePathAsync(ViewModel.SdPath!);
            MovablePathBox.Text = ViewModel.MovablePath;
        }

        _suppressSelectionChanged = false;
        SdDriveComboBox.SelectedIndex = 0;
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
}