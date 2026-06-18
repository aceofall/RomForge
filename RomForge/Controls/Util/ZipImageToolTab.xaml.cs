using PickPack.Disk;
using RomForge.Core.Services;
using RomForge.ViewModels.Util;
using System.Windows;
using System.Windows.Controls;

namespace RomForge.Controls.Util;

public partial class ZipImageToolTab : UserControl
{
    private UsbDetector? _usbDetector;
    private readonly bool _suppressSelectionChanged;

    private ZipImageToolMainViewModel? ViewModel => DataContext as ZipImageToolMainViewModel;

    public ZipImageToolTab()
    {
        InitializeComponent();
        Loaded += ZipImageToolTab_Loaded;
    }

    private void ZipImageToolTab_Loaded(object sender, RoutedEventArgs e)
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

        var infos = await Task.Run(() => DriveInfos.GetDriveInfos());

        ViewModel.UsbDrives.Clear();

        foreach (var info in infos)
            ViewModel.UsbDrives.Add(info);

        if (ViewModel.UsbDrives.Count > 0)
            ViewModel.SelectedDrive = ViewModel.UsbDrives[0];
    }
}