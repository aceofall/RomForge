using NSW.Core.Enums;
using RomForge.ViewModels.WiiU;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomForge.Controls.WiiU
{
    public partial class RepackTab : UserControl
    {
        RepackMainViewModel ViewModel => (RepackMainViewModel)DataContext;

        public RepackTab()
        {
            InitializeComponent();
        }

        private void Root_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Root_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Handled = true;
                return;
            }

            string[]? items = (string[]?)e.Data.GetData(DataFormats.FileDrop);

            if (items is null || items.Length == 0)
            {
                e.Handled = true;
                return;
            }

            string? lastFolder = null;
            string? lastFile = null;

            foreach (var item in items)
            {
                if (Directory.Exists(item))
                {
                    lastFolder = item;
                    continue;
                }

                string extension = Path.GetExtension(item);

                if (string.Equals(extension, ".wud", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".wux", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".wua", StringComparison.OrdinalIgnoreCase))
                {
                    lastFile = item;
                }
            }

            if (lastFolder is not null && (lastFile is null || Array.IndexOf(items, lastFolder) > Array.IndexOf(items, lastFile)))
                ViewModel.AddFolder(lastFolder);
            else if (lastFile is not null)
                await ViewModel.AddFileAsync(lastFile);

            e.Handled = true;
        }

        private void LvFiles_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;

            var selected = ViewModel?.SelectedEntry;

            ViewModel?.Entries.Remove(selected);
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
}