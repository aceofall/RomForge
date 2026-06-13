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
    private string? _lastSortColumn;
    private ListSortDirection _lastSortDirection;

    private MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        Closing += MainWindow_Closing;

        ViewModel.CompressVM.ScrollToItemRequested += item => Dispatcher.InvokeAsync(() => lvFiles.ScrollIntoView(item), DispatcherPriority.Background);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        int value = 1;

        _ = Win32API.DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        bool busy = ViewModel.CompressVM.IsConverting || ViewModel.PatchVM.IsPatching;

        if (!busy) 
            return;

        var result = MessageBox.Show("작업이 진행 중입니다. 취소하고 종료할까요?", "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            ViewModel.CompressVM.CancelCommand.Execute(null);
            ViewModel.PatchVM.CancelCommand.Execute(null);
        }
        else
            e.Cancel = true;
    }

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
        if (e.Key != Key.Delete) 
            return;

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
        if (lvFiles.SelectedItems.Count == 0) 
            e.Handled = true;
    }

    private void MenuItem_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var selected = lvFiles.SelectedItems.Cast<FileItemViewModel>().ToList();

        CompressViewModel.OpenFolder(selected);
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(ViewModel) { Owner = this };

        settings.ShowDialog();
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header)
            return;

        if (header.Tag is not string sortBy) 
            return;

        var direction = _lastSortColumn == sortBy && _lastSortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        var view = CollectionViewSource.GetDefaultView(lvFiles.ItemsSource);

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(sortBy, direction));
        view.Refresh();

        _lastSortColumn = sortBy;
        _lastSortDirection = direction;
    }

    private static string? OpenSingleFileDialog(string title)
    {
        var dlg = new OpenFileDialog { Title = title };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    // 일반 탭
    private void NormalSourceDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var path = OpenSingleFileDialog("원본 파일 선택");
        if (path != null)
            ViewModel.PatchVM.NormalVM.SourcePath = path;
    }

    private void NormalSourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.NormalVM.SourcePath = files[0];
    }

    private void NormalPatchDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "패치 파일 선택" };
        if (dlg.ShowDialog() == true)
            ViewModel.PatchVM.NormalVM.PatchPath = dlg.FileName;
    }

    private void NormalPatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.NormalVM.PatchPath = files[0];
    }

    // 아케이드 탭
    private void ArcadeSourceDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var path = OpenSingleFileDialog("원본 ZIP 선택");
        if (path != null)
            ViewModel.PatchVM.ArcadeVM.SourcePath = path;
    }

    private void ArcadeSourceDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ViewModel.PatchVM.ArcadeVM.SourcePath = files[0];
    }

    private void ArcadePatchDrop_Click(object sender, MouseButtonEventArgs e)
    {
        // 파일 먼저 시도
        var dlg = new OpenFileDialog { Title = "패치 파일 선택" };
        if (dlg.ShowDialog() == true)
        {
            ViewModel.PatchVM.ArcadeVM.PatchPath = dlg.FileName;
            return;
        }

        // 폴더 선택
        var folderDlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "패치 폴더 선택",
            UseDescriptionForTitle = true
        };
        if (folderDlg.ShowDialog() == true)
            ViewModel.PatchVM.ArcadeVM.PatchPath = folderDlg.SelectedPath;
    }

    private void ArcadePatchDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        ViewModel.PatchVM.ArcadeVM.PatchPath = files[0]; // 파일이든 폴더든 그대로
    }

    // 카드 드롭
    private void MatchCard_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MatchCard_PatchDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.Tag is not ArcadeMatchItem item) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        ViewModel.PatchVM.ArcadeVM.ManualMatch(item, files[0]);
    }
}