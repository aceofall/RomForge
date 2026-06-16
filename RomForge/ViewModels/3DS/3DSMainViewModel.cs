using Common.WPF.ViewModels;
using RomForge.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace RomForge.ViewModels._3DS;

public class _3DSMainViewModel : ToolTabViewModel
{
    private int _subTabIndex;

    public InstallerMainViewModel InstallerVM { get; }
    public ConverterMainViewModel ConverterVM { get; }

    public int SubTabIndex
    {
        get => _subTabIndex;
        set
        {
            _subTabIndex = value;
            OnPropertyChanged();
            SyncLogEntries();
        }
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public _3DSMainViewModel()
    {
        InstallerVM = new InstallerMainViewModel();
        ConverterVM = new ConverterMainViewModel();

        RegisterChild(InstallerVM);
        RegisterChild(ConverterVM);

        InstallerVM.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
        ConverterVM.LogEntries.CollectionChanged += LogEntries_CollectionChanged;

        SyncLogEntries();
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var targetCollection = _subTabIndex == 0 ? InstallerVM.LogEntries : ConverterVM.LogEntries;

        if (sender != targetCollection) 
            return;

        if (Application.Current?.Dispatcher != null)
            Application.Current.Dispatcher.Invoke(() => HandleCollectionChanged(e));
        else
            HandleCollectionChanged(e);
    }

    private void HandleCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    foreach (LogEntry item in e.NewItems)
                        LogEntries.Add(item);
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (LogEntry item in e.OldItems)
                        LogEntries.Remove(item);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                LogEntries.Clear();
                break;
        }
    }

    private void SyncLogEntries()
    {
        if (Application.Current?.Dispatcher != null)
            Application.Current.Dispatcher.Invoke(() => DoSync());
        else
            DoSync();
    }

    private void DoSync()
    {
        var currentSource = _subTabIndex == 0 ? InstallerVM.LogEntries : ConverterVM.LogEntries;

        foreach (var item in currentSource)
            LogEntries.Add(item);
    }
}