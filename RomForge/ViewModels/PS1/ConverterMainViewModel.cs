using Common;
using Common.WPF.ViewModels;
using RomForge.Helpers;
using RomForge.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels.PS1;

public class ConverterMainViewModel: ToolTabViewModel
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".m3u", ".iso", ".chd"
    };

    private bool _isConverting;
    public bool IsConverting
    {
        get => _isConverting;
        set { _isConverting = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private CancellationTokenSource _cts = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }

    public ConverterMainViewModel()
    {
        CancelCommand = new RelayCommand(_ => _cts.Cancel(), _ => IsConverting);
    }

    private void AppendLog(string msg, LogLevel level = LogLevel.Info)
    {
        if (Application.Current?.Dispatcher == null) return;

        Application.Current.Dispatcher.Invoke(() =>
            LogEntries.Add(new LogEntry { Message = msg, Level = level })
        );
    }

    private void ClearLog()
    {
        if (Application.Current?.Dispatcher == null) return;

        Application.Current.Dispatcher.Invoke(() => LogEntries.Clear());
    }

    public static string GetFileDialogFilter()
    {
        string wildcards = string.Join(";", SupportedExtensions.Select(ext => $"*{ext}"));
        return $"지원 파일|{wildcards}|모든 파일|*.*";
    }
}