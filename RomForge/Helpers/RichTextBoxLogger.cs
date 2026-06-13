using Common;
using RomForge.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RomForge.Helpers;

public static class RichTextBoxLogger
{
    public static readonly DependencyProperty LogEntriesProperty = DependencyProperty.RegisterAttached("LogEntries", typeof(ObservableCollection<LogEntry>), typeof(RichTextBoxLogger), new PropertyMetadata(null, OnLogEntriesChanged));

    public static void SetLogEntries(DependencyObject obj, ObservableCollection<LogEntry> value) => obj.SetValue(LogEntriesProperty, value);

    public static ObservableCollection<LogEntry> GetLogEntries(DependencyObject obj) => (ObservableCollection<LogEntry>)obj.GetValue(LogEntriesProperty);

    private static void OnLogEntriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox rtb) 
            return;

        rtb.Document.Blocks.Clear();
        rtb.Document.FontFamily = new FontFamily("Consolas");
        rtb.Document.FontSize = 12;
        rtb.Document.PagePadding = new Thickness(0);
        rtb.Document.LineHeight = 18;

        if (e.NewValue is ObservableCollection<LogEntry> entries)
            entries.CollectionChanged += (_, args) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    switch (args.Action)
                    {
                        case NotifyCollectionChangedAction.Reset:
                            rtb.Document.Blocks.Clear();
                            break;

                        case NotifyCollectionChangedAction.Add:
                            if (args.NewItems != null)
                            {
                                foreach (LogEntry entry in args.NewItems)
                                    AppendEntry(rtb, entry);
                            }
                            break;
                    }
                });
            };
    }

    private static void AppendEntry(RichTextBox rtb, LogEntry entry)
    {

        rtb.Document.Blocks.Add(new Paragraph(new Run(entry.Message))
        {
            Foreground = new SolidColorBrush(GetColor(entry.Level)),
            Margin = new Thickness(0)
        });

        rtb.ScrollToEnd();
    }

    private static Color GetColor(LogLevel level) => level switch
    {
        LogLevel.Ok => Color.FromRgb(100, 200, 100),
        LogLevel.Highlight => Color.FromRgb(255, 200, 0),
        LogLevel.Error => Color.FromRgb(255, 80, 80),
        _ => Color.FromRgb(180, 180, 180),
    };
}