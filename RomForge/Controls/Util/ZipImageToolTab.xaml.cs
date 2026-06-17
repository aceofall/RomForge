using PickPack.Disk;
using RomForge.Core.Services;
using RomForge.ViewModels.Util;
using System.Windows;
using System.Windows.Controls;

namespace RomForge.Controls.Util;

public partial class ZipImageToolTab : UserControl
{
    private UsbDetector? _usbDetector;
    private bool _suppressSelectionChanged;
    private ZipImageToolViewModel? ViewModel => DataContext as ZipImageToolViewModel;

    public ZipImageToolTab()
    {
        InitializeComponent();
        Loaded += ZipImageToolTab_Loaded;
    }

    private void ZipImageToolTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_usbDetector != null) return;
        if (ViewModel == null) return;

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
        if (ViewModel == null) return;

        _suppressSelectionChanged = true;
        UsbComboBox.Items.Clear();

        var infos = await Task.Run(() => DriveInfos.GetDriveInfos());

        foreach (var info in infos)
            UsbComboBox.Items.Add(info);

        if (UsbComboBox.Items.Count == 0)
        {
            ViewModel.SelectedDrive = null;
            UsbComboBox.IsEnabled = false;
            _suppressSelectionChanged = false;
            return;
        }

        UsbComboBox.IsEnabled = true;
        _suppressSelectionChanged = false;
        UsbComboBox.SelectedIndex = 0;
    }

    private void UsbComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged || ViewModel == null) return;
        if (UsbComboBox.SelectedItem is not DriveInfos selected) return;

        ViewModel.SelectedDrive = selected;
    }
}