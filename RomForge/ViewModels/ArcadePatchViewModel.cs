using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows;

namespace RomForge.ViewModels;

public class ArcadePatchViewModel : ViewModelBase
{
    public ObservableCollection<ArcadeMatchItem> MatchItems { get; } = [];

    private string? _sourcePath;
    public string? SourcePath
    {
        get => _sourcePath;
        set
        {
            _sourcePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceLabel));

            if (value is not null) 
                Analyze();
        }
    }

    private string? _patchPath;
    public string? PatchPath
    {
        get => _patchPath;
        set
        {
            _patchPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PatchLabel));

            if (value is not null) 
                Analyze();
        }
    }

    public string SourceLabel => SourcePath ?? "원본 ZIP을 드래그하거나 클릭하세요";

    public string PatchLabel => PatchPath ?? "패치 파일 / ZIP / 폴더를 드래그하거나 클릭하세요";

    public Visibility HintVisibility => MatchItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private int _totalProgress;
    public int TotalProgress
    {
        get => _totalProgress;
        set { _totalProgress = value; OnPropertyChanged(); }
    }

    private string _progressSummary = string.Empty;
    public string ProgressSummary
    {
        get => _progressSummary;
        set { _progressSummary = value; OnPropertyChanged(); }
    }

    public void ManualMatch(ArcadeMatchItem item, string patchFilePath)
    {
        item.PatchPath = patchFilePath;
        item.PatchFileName = Path.GetFileName(patchFilePath);
        UpdateSummary();
    }

    public void UpdateTotalProgress()
    {
        if (MatchItems.Count == 0) 
        { 
            TotalProgress = 0; 
            return; 
        }

        TotalProgress = (int)MatchItems.Average(x => x.Progress);
    }

    public void UpdateSummary()
    {
        int matched = MatchItems.Count(x => x.IsMatched);

        ProgressSummary = $"{matched} / {MatchItems.Count} 매칭";
    }

    public void Clear()
    {
        SourcePath = null;
        PatchPath = null;
        MatchItems.Clear();
        TotalProgress = 0;
        ProgressSummary = string.Empty;
        OnPropertyChanged(nameof(HintVisibility));
    }

    private void Analyze()
    {
        if (SourcePath is null || PatchPath is null) 
            return;

        MatchItems.Clear();

        // 원본 ZIP 엔트리 수집
        var sourceEntries = GetSourceEntries(SourcePath);

        // 패치 파일 수집
        var patchFiles = GetPatchFiles(PatchPath);

        // 매칭
        foreach (var (fileName, fullPath) in sourceEntries)
        {
            var ext = Path.GetExtension(fileName).TrimStart('.').ToLower();

            // 1순위: 베이스네임 매칭
            var matched = patchFiles.FirstOrDefault(p =>
                Path.GetFileNameWithoutExtension(p).Equals(
                    Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase));

            // 2순위: 확장자 키 매칭 (패치파일 베이스네임의 마지막 확장자 부분)
            matched ??= patchFiles.FirstOrDefault(p =>
                    Path.GetFileNameWithoutExtension(p).EndsWith(
                        $"-{ext}", StringComparison.OrdinalIgnoreCase));

            MatchItems.Add(new ArcadeMatchItem
            {
                SourceFileName = fileName,
                SourcePath = fullPath,
                PatchPath = matched,
                PatchFileName = matched is null ? null : Path.GetFileName(matched),
            });
        }

        UpdateSummary();
        OnPropertyChanged(nameof(HintVisibility));
    }

    private static List<(string fileName, string fullPath)> GetSourceEntries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        return [.. zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => (e.Name, $"{zipPath}|{e.FullName}"))];
    }

    private static List<string> GetPatchFiles(string path)
    {
        // ZIP
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            using var zip = ZipFile.OpenRead(path);
            return [.. zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => $"{path}|{e.FullName}")];
        }

        // 단일 파일
        if (File.Exists(path))
            return [path];

        // 폴더
        if (Directory.Exists(path))
            return [.. Directory.GetFiles(path)];

        return [];
    }
}