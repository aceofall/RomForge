using System.Windows.Controls;
using RomForge.ViewModels.Util;

namespace RomForge.Controls.Util;

public partial class ZipImageToolTab : UserControl
{
    private ZipImageToolViewModel? ViewModel => DataContext as ZipImageToolViewModel;

    public ZipImageToolTab()
    {
        InitializeComponent();
    }
}