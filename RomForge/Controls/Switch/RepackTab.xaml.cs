using NSW.Core.Enums;
using RomForge.ViewModels.Switch;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Path = System.IO.Path;

namespace RomForge.Controls.Switch;

public partial class RepackTab : UserControl
{
    RepackMainViewModel ViewModel => (RepackMainViewModel)DataContext;

    private CancellationTokenSource? _cts;

    public RepackTab()
    {
        InitializeComponent();
        Loaded += RepackTab_Loaded;
    }

    private async void RepackTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            return;

        fileMgr.FileListChanged += SyncAll;

        SyncAll();
    }

    private async void SyncAll()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            await Task.Delay(300, _cts.Token);
            await langTab.UpdateLanguageTabAsync(fileMgr.GameFiles, Path.Combine(ViewModel.OutputPath, "unpacked"));

            SyncContext();
        }
        catch (OperationCanceledException) { }
    }

    private void SyncContext()
        => ViewModel.Context = new RepackMainViewModel.BuildContext(fileMgr.GameFiles, langTab.CurrentMetadata, langTab.ForcedLanguage, langTab.TargetIdOffset);

    private void TxtPatch_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TxtPatch_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TxtPatch_Drop(object sender, DragEventArgs e)
    {
        var items = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        var folder = items?.FirstOrDefault(Directory.Exists);

        if (folder != null) 
            ViewModel.PatchPath = folder;

        e.Handled = true;
    }

    private void TxtDlc_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TxtDlc_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TxtDlc_Drop(object sender, DragEventArgs e)
    {
        var items = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        var folder = items?.FirstOrDefault(Directory.Exists);

        if (folder != null) 
            ViewModel.DlcPath = folder;

        e.Handled = true;
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLocked) 
        { 
            ViewModel.Cancel(); 
            return;
        }

        await RecalcIfNeededAsync();

        langTab.SyncMetadataFromUI();
        SyncContext();

        await ViewModel.StartAsync(BuildMode.FullProcess);
    }

    private async void BtnUnpack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLocked) 
        { 
            ViewModel.Cancel(); 
            return;
        }

        await RecalcIfNeededAsync();

        SyncContext();

        await ViewModel.StartAsync(BuildMode.UnpackOnly);
    }

    private void BtnRebuild_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLocked)
        { 
            ViewModel.Cancel(); 
            return; 
        }

        SyncContext();

        _ = ViewModel.StartAsync(BuildMode.RebuildOnly);
    }

    private async Task RecalcIfNeededAsync()
    {
        if (!fileMgr.GameFiles.Any(f => f.IsKeyMissing)) 
            return;

        var tcs = new TaskCompletionSource();

        fileMgr.RecalcKeyMissingFiles(async () =>
        {
            await langTab.UpdateLanguageTabAsync(fileMgr.GameFiles);
            tcs.TrySetResult();
        });

        await tcs.Task;
    }
}