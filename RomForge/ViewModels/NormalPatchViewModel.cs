namespace RomForge.ViewModels;

public class NormalPatchViewModel : ViewModelBase
{
    private string? _sourcePath;
    public string? SourcePath
    {
        get => _sourcePath;
        set
        {
            _sourcePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceLabel));
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
        }
    }

    public string SourceLabel => SourcePath ?? "원본 파일을 드래그하거나 클릭하세요";

    public string PatchLabel => PatchPath ?? "패치 파일을 드래그하거나 클릭하세요";

    private int _progress;
    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#888888";
    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public void Clear()
    {
        SourcePath = null;
        PatchPath = null;
        Progress = 0;
        StatusText = string.Empty;
    }
}