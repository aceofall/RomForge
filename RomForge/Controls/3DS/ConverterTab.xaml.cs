using RomForge.ViewModels._3DS;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace RomForge.Controls._3DS;

public partial class ConverterTab : UserControl
{
    private ConverterMainViewModel? ViewModel => DataContext as ConverterMainViewModel;

    public ConverterTab()
    {
        InitializeComponent();

        DataContextChanged += ConverterTab_DataContextChanged;
    }

    private void ConverterTab_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ConverterMainViewModel oldVm)
            oldVm.ScrollToItemRequested -= OnScrollToItemRequested;

        if (e.NewValue is ConverterMainViewModel newVm)
            newVm.ScrollToItemRequested += OnScrollToItemRequested;
    }

    private void OnScrollToItemRequested(FileItemViewModel item)
    {
        Dispatcher.InvokeAsync(() =>
        {
            lvFiles.ScrollIntoView(item);
        }, DispatcherPriority.Background);
    }

    private void LvFiles_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void LvFiles_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        ViewModel?.AddPaths(paths);
    }

    private void LvFiles_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) 
            return;

        if (ViewModel == null) 
            return;

        var selected = lvFiles.SelectedItems.Cast<FileItemViewModel>().ToList();
        ViewModel.RemoveItems(selected);
    }

    private string? _lastSortColumn;
    private ListSortDirection _lastSortDirection;

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header)
            return;

        if (header.Tag is not string sortBy)
            return;

        var direction =
            _lastSortColumn == sortBy &&
            _lastSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

        ICollectionView dataView = CollectionViewSource.GetDefaultView(lvFiles.ItemsSource);

        if (dataView == null) 
            return;

        dataView.SortDescriptions.Clear();
        dataView.SortDescriptions.Add(new SortDescription(sortBy, direction));
        dataView.Refresh();

        _lastSortColumn = sortBy;
        _lastSortDirection = direction;
    }

    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = ConverterMainViewModel.GetFileDialogFilter()
        };

        if (dialog.ShowDialog() == true)
            ViewModel?.AddPaths(dialog.FileNames);
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "추가할 폴더를 선택하세요",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ViewModel?.AddPaths([dialog.SelectedPath]);
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) 
            return;

        var selected = lvFiles.SelectedItems.Cast<FileItemViewModel>().ToList();
        ViewModel.RemoveItems(selected);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ClearItems();
    }
}