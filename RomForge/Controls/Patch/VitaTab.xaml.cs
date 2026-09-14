using Ookii.Dialogs.Wpf;
using RomForge.ViewModels;
using RomForge.ViewModels.Patch;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomForge.Controls.Patch
{
    public partial class VitaTab : UserControl
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        public VitaTab()
        {
            InitializeComponent();
        }

        private void LvPkg_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
        }

        private void Root_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void Root_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    if (ViewModel?.PatchVM.VitaVM != null)
                    {
                        foreach (var file in files)
                            ViewModel.PatchVM.VitaVM.AddSourceFile(file);
                    }
                }
            }
            e.Handled = true;
        }

        private void LvPkg_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (ViewModel?.PatchVM.VitaVM?.RemoveSelectedCommand?.CanExecute(null) == true)
                    ViewModel.PatchVM.VitaVM.RemoveSelectedCommand.Execute(null);
            }
        }

        private void License_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void License_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    if (sender is TextBox tb && tb.DataContext is VitaSourceRowViewModel row)
                        row.License = files[0];
                }
            }
            e.Handled = true;
        }

        private void PatchDropTarget_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void PatchDropTarget_DragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void PatchDropTarget_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    string path = files[0];

                    if (Directory.Exists(path) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                    {
                        if (sender is FrameworkElement { Tag: VitaSourceRowViewModel row })
                            row.PatchPath = path;
                    }
                }
            }
            e.Handled = true;
        }

        private void PatchDropTarget_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                if (element.ContextMenu != null)
                {
                    element.ContextMenu.PlacementTarget = element;
                    element.ContextMenu.IsOpen = true;
                }
            }
        }

        private void PatchMenu_SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is VitaSourceRowViewModel rowVm)
            {
                var dlg = new VistaFolderBrowserDialog
                {
                    Description = "한글 패치 폴더 선택",
                    UseDescriptionForTitle = true
                };

                if (dlg.ShowDialog() == true)
                    rowVm.PatchPath = dlg.SelectedPath;
            }
        }

        private void PatchMenu_SelectArchive_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is VitaSourceRowViewModel rowVm)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "한글 패치 압축파일 선택",
                    Filter = "Archive Files (*.zip;*.7z)|*.zip;*.7z|All Files (*.*)|*.*"
                };

                if (dlg.ShowDialog() == true)
                    rowVm.PatchPath = dlg.FileName;
            }
        }

        private void MenuItem_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is VitaSourceRowViewModel row)
            {
                if (!string.IsNullOrEmpty(row.Path))
                {
                    string? dir = File.Exists(row.Path) ? Path.GetDirectoryName(row.Path) : row.Path;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true
                        });
                    }
                }
            }
        }
    }
}