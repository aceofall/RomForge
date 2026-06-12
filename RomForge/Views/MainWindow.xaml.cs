using Microsoft.Win32;
using RomForge.Helpers;
using RomForge.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RomForge.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        Closing += MainWindow_Closing;

        ViewModel.CompressVM.ScrollToItemRequested += item =>
            Dispatcher.InvokeAsync(() => lvFiles.ScrollIntoView(item), DispatcherPriority.Background);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        int value = 1;
        Win32API.DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int));
    }

    // ── 종료 확인 ──
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        bool busy = ViewModel.CompressVM.IsConverting || ViewModel.PatchVM.IsPatching;
        if (!busy) return;

        var result = MessageBox.Show(
            "작업이 진행 중입니다. 취소하고 종료할까요?",
            "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            ViewModel.CompressVM.CancelCommand.Execute(null);
            ViewModel.PatchVM.CancelCommand.Execute(null);
        }
        else
        {
            e.Cancel = true;
        }
    }

    // ── 패치탭 드롭존 ──
    private void SourceDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var path = OpenSingleFileDialog("원본 파일 선택");
        if (path != null) ViewModel.PatchVM.SourcePath = path;
    }

    private void SourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.SourcePath = files[0];
    }

    private void PatchDrop_Click(object sender, MouseButtonEventArgs e)
    {
        // 파일 또는 폴더 선택
        var dlg = new OpenFileDialog { Title = "패치 파일 선택" };
        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.PatchPath = dlg.FileName;
    }

    private void PatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.PatchPath = files[0];
    }

    private void BtnPatchClear_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PatchVM.SourcePath = null;
        ViewModel.PatchVM.PatchPath = null;
    }

    // ── 압축탭 ──
    private void LvFiles_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void LvFiles_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            ViewModel.CompressVM.AddPaths(paths);
    }

    private void LvFiles_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        var selected = lvFiles.SelectedItems.Cast<FileItemViewModel>().ToList();
        ViewModel.CompressVM.RemoveItems(selected);
    }

    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = CompressViewModel.GetFileDialogFilter()
        };
        if (dlg.ShowDialog() == true)
            ViewModel.CompressVM.AddPaths(dlg.FileNames);
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "추가할 폴더를 선택하세요",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == true)
            ViewModel.CompressVM.AddPaths([dlg.SelectedPath]);
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        var selected = lvFiles.SelectedItems.Cast<FileItemViewModel>().ToList();
        ViewModel.CompressVM.RemoveItems(selected);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e) => ViewModel.CompressVM.ClearItems();

    private void LvFiles_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (lvFiles.SelectedItems.Count == 0) e.Handled = true;
    }

    private void MenuItem_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var selected = lvFiles.SelectedItems.Cast<FileItemViewModel>().ToList();
        CompressViewModel.OpenFolder(selected);
    }

    // ── 공용 ──
    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(ViewModel) { Owner = this };
        settings.ShowDialog();
    }

    private string? _lastSortColumn;
    private System.ComponentModel.ListSortDirection _lastSortDirection;

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        if (header.Tag is not string sortBy) return;

        var direction = _lastSortColumn == sortBy && _lastSortDirection == System.ComponentModel.ListSortDirection.Ascending
            ? System.ComponentModel.ListSortDirection.Descending
            : System.ComponentModel.ListSortDirection.Ascending;

        var view = CollectionViewSource.GetDefaultView(lvFiles.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(sortBy, direction));
        view.Refresh();

        _lastSortColumn = sortBy;
        _lastSortDirection = direction;
    }

    private static string? OpenSingleFileDialog(string title)
    {
        var dlg = new OpenFileDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}