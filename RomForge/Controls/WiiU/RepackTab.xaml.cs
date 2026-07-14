using NSW.Core.Enums;
using RomForge.ViewModels.WiiU;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomForge.Controls.WiiU;

public partial class RepackTab : UserControl
{
    RepackMainViewModel ViewModel => (RepackMainViewModel)DataContext;

    public RepackTab()
    {
        InitializeComponent();
    }

    private void LvFiles_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        while (true)
        {
            if (ViewModel?.SelectedEntry is not null)
                ViewModel?.Entries.Remove(ViewModel?.SelectedEntry);
            else
                break;
        }
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLocked)
        {
            ViewModel.Cancel();
            return;
        }

        await ViewModel.StartAsync(BuildMode.FullProcess);
    }

    private async void BtnUnpack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLocked)
        {
            ViewModel.Cancel();
            return;
        }

        await ViewModel.StartAsync(BuildMode.UnpackOnly);
    }

    private async void BtnRebuild_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLocked)
        {
            ViewModel.Cancel();
            return;
        }

        await ViewModel.StartAsync(BuildMode.RebuildOnly);
    }
}