namespace RomForge.ViewModels;

public class ArcadeMatchItem : ViewModelBase
{
    public string SourceFileName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;

    private string? _patchFileName;
    public string? PatchFileName
    {
        get => _patchFileName;
        set
        {
            _patchFileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMatched));
        }
    }

    private string? _patchPath;
    public string? PatchPath
    {
        get => _patchPath;
        set { _patchPath = value; OnPropertyChanged(); }
    }

    private int _progress;
    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#888888";
    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public bool IsMatched => PatchFileName is not null;
}