using Microsoft.Win32;
using RomForge.ViewModels._3DS;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomForge.Controls._3DS;

public partial class InstallTab : UserControl
{
    private InstallerMainViewModel Vm => (InstallerMainViewModel)DataContext;
    private CancellationTokenSource? _installCts;

    public InstallTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is InstallerMainViewModel vm)
                InstallListView.ItemsSource = vm.Install.Items;
        };
    }

    private async void InstallList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        var allFiles = files
            .SelectMany(path => Directory.Exists(path) ? GetFilesSafe(path) : [path])
            .ToList();

        await Vm.Install.AddFiles(allFiles);
    }

    private void InstallList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void InstallList_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
            Vm.Install.RemoveItems(InstallListView.SelectedItems.Cast<TitleViewModel>());
    }

    private async void InstallAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "설치할 파일 선택",
            Filter = "3DS 파일|*.cia;*.3ds;*.cci;*.zcci|모든 파일|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;
        await Vm.Install.AddFiles(dialog.FileNames);
    }

    private async void InstallAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "폴더 선택 (폴더 안의 아무 파일이나 선택)",
            CheckFileExists = false,
            FileName = "폴더 선택"
        };

        if (dialog.ShowDialog() != true) return;

        string folder = Path.GetDirectoryName(dialog.FileName)!;
        await Vm.Install.AddFiles(GetFilesSafe(folder));
    }

    private void InstallRemove_Click(object sender, RoutedEventArgs e) =>
        Vm.Install.RemoveItems(InstallListView.SelectedItems.Cast<TitleViewModel>());

    private void InstallClear_Click(object sender, RoutedEventArgs e) =>
        Vm.Install.Clear();

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        _installCts = new CancellationTokenSource();
        Vm.Install.IsInstalling = true;

        try
        {
            foreach (var item in InstallListView.Items)
            {
                if (item is not TitleViewModel title) continue;
                if (_installCts.IsCancellationRequested) continue;
                await Vm.InstallAsync(title, _installCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"설치 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _installCts.Dispose();
            _installCts = null;
            Vm.Install.IsInstalling = false;
        }
    }

    private void InstallCancel_Click(object sender, RoutedEventArgs e) => _installCts?.Cancel();

    private static List<string> GetFilesSafe(string folder)
    {
        var files = new List<string>();
        try { files.AddRange(Directory.GetFiles(folder)); } catch { }
        foreach (var subDir in Directory.GetDirectories(folder))
            try { files.AddRange(GetFilesSafe(subDir)); } catch { }
        return files;
    }
}