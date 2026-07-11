using Common.WPF.ViewModels;

namespace RomForge.Core.Models.WiiU;

public sealed class TitleInputEntry : ViewModelBase
{
    public string Path { get; init; } = "";

    public bool IsFolder { get; init; }

    public int SubTitleIndex { get; init; }

    public string Kind { get; init; } = "알수없음";

    public string TitleIdHex { get; init; } = "0000000000000000";

    public int TitleVersion { get; init; }

    public int FileCount { get; init; }

    private string? _patchPath;
    public string? PatchPath
    {
        get => _patchPath;
        set { _patchPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(PatchDisplay)); }
    }

    public string Summary => $"{TitleIdHex}_v{TitleVersion}  ({FileCount:N0}개 파일)";

    public string SourceDisplay => IsFolder ? Path : System.IO.Path.GetFileName(Path);

    public string PatchDisplay => string.IsNullOrEmpty(PatchPath) ? "(없음)" : System.IO.Path.GetFileName(PatchPath);

    public static string GuessKind(string titleIdHex)
    {
        if (titleIdHex.Length < 8) 
            return "알수없음";

        return titleIdHex[..8].ToLowerInvariant() switch
        {
            "00050000" => "베이스",
            "0005000e" => "업데이트",
            "0005000c" => "DLC",
            _ => "알수없음",
        };
    }
}