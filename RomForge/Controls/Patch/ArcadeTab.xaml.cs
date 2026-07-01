using RomForge.Core.Models.Patch;
using RomForge.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RomForge.Controls.Patch;

public partial class ArcadeTab : UserControl
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public ArcadeTab()
    {
        InitializeComponent();
    }

    private void ArcadeSourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.ArcadeVM.SourcePath = files[0];
    }

    private void ArcadePatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        ViewModel.PatchVM.ArcadeVM.PatchPath = files[0];
    }

    private void MatchCard_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MatchCard_PatchDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border)
            return;

        if (border.Tag is not ArcadeMatchItem item)
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        string path = files[0];
        var patchEntry = new PatchEntry
        {
            DisplayName = Path.GetFileName(path),
            EntryPath = path
        };

        ViewModel.PatchVM.ArcadeVM.ManualMatch(item, patchEntry);
    }

    private void PatchPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;

        if (combo.Tag is not ArcadeMatchItem item)
            return;

        if (combo.SelectedItem is not PatchEntry entry)
            return;

        if (ReferenceEquals(entry, item.PatchEntry))
            return;

        ViewModel.PatchVM.ArcadeVM.ManualMatch(item, entry);
    }
}