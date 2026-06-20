using Common;
using Common.WPF.ViewModels;
using PBP.Core.Services;
using RomForge.Helpers;
using RomForge.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels.PS1;

public class ConverterMainViewModel : ToolTabViewModel
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
    public ObservableCollection<CompressFileItem> FileItems { get; } = [];

    public Visibility HintVisibility => FileItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }

    public ConverterMainViewModel()
    {
        RunCommand = new RelayCommand(async _ => await RunAsync(), _ => !IsConverting && FileItems.Count > 0);
        CancelCommand = new RelayCommand(_ => _cts.Cancel(), _ => IsConverting);
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var existing = FileItems.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ExpandPaths(paths))
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(path)))
                continue;

            if (!existing.Add(path))
                continue;

            FileItems.Add(new CompressFileItem(path) { No = FileItems.Count + 1 });
        }

        OnPropertyChanged(nameof(HintVisibility));
        CommandManager.InvalidateRequerySuggested();
    }

    public void RemoveItems(IEnumerable<CompressFileItem> items)
    {
        foreach (var item in items.ToList())
            FileItems.Remove(item);

        OnPropertyChanged(nameof(HintVisibility));
    }

    public void ClearItems()
    {
        FileItems.Clear();
        OnPropertyChanged(nameof(HintVisibility));
    }

    private async Task RunAsync()
    {
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        ClearLog();
        IsConverting = true;

        try
        {
            var cnt = 0;

            foreach (var item in FileItems)
            {
                if (_cts.Token.IsCancellationRequested) break;
                if (item.Status == "완료") continue;

                item.Status = "변환중";
                item.Progress = 0;

                var progress = new Progress<ProgressInfo>(p => item.Progress = p.Percent);

                try
                {
                    var ext = Path.GetExtension(item.FilePath).ToLowerInvariant();

                    if (ext == ".m3u")
                    {
                        var (discs, mainTitle) = ParseM3u(item.FilePath);
                        var outputPath = Path.Combine(Path.GetDirectoryName(item.FilePath)!, $"{mainTitle}.pbp");

                        await PbpPackager.WriteMultiDiscAsync(discs, mainTitle, outputPath, 9,
                            progress, (msg, lvl, id) => AppendLog(msg, lvl), _cts.Token);
                    }
                    else if (ext == ".chd")
                    {
                        throw new NotSupportedException("CHD는 아직 지원하지 않아요.");
                    }
                    else
                    {
                        var title = Path.GetFileNameWithoutExtension(item.FilePath);
                        await PbpPackager.WriteSingleDiscAsync(item.FilePath, title, 9,
                            progress, (msg, lvl, id) => AppendLog(msg, lvl), _cts.Token);
                    }

                    item.Progress = 100;
                    item.Status = "완료";
                    cnt++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "취소";
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = "실패";
                    AppendLog($"[{item.FileName}] {ex.Message}", LogLevel.Error);
                }
            }

            if (cnt > 0)
                AppendLog($"총 {cnt}개의 작업을 완료했습니다.", LogLevel.Ok);
        }
        catch (OperationCanceledException)
        {
            AppendLog("작업이 취소되었습니다.", LogLevel.Error);
        }
        finally
        {
            IsConverting = false;
        }
    }

    /// <summary>
    /// m3u는 디스크 경로를 줄 단위로 나열한 텍스트 파일.
    /// 메인 타이틀은 파일명에서 "(Disc N)" 패턴을 제거해서 추정 — 정확한 타이틀이 필요하면 나중에 직접 수정 가능하게 UI에서 받아야 함.
    /// </summary>
    private static (List<(string IsoPath, string GameTitle)> Discs, string MainTitle) ParseM3u(string m3uPath)
    {
        var dir = Path.GetDirectoryName(m3uPath)!;
        var lines = File.ReadAllLines(m3uPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        var discs = new List<(string, string)>();
        foreach (var line in lines)
        {
            var rawPath = Path.IsPathRooted(line) ? line : Path.Combine(dir, line);
            var path = Path.GetExtension(rawPath).Equals(".cue", StringComparison.OrdinalIgnoreCase)
                ? CueFileResolver.GetBinPath(rawPath)
                : rawPath;

            discs.Add((path, Path.GetFileNameWithoutExtension(rawPath)));
        }

        var mainTitle = System.Text.RegularExpressions.Regex.Replace(
            Path.GetFileNameWithoutExtension(m3uPath), @"\s*\(Disc\s*\d+\)", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        return (discs, mainTitle);
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
        };

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                foreach (var f in Directory.EnumerateFiles(path, "*.*", opts))
                    yield return f;
            else if (File.Exists(path))
                yield return path;
        }
    }

    private void AppendLog(string msg, LogLevel level = LogLevel.Info)
    {
        if (Application.Current?.Dispatcher == null) return;
        Application.Current.Dispatcher.Invoke(() => LogEntries.Add(new LogEntry { Message = msg, Level = level }));
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