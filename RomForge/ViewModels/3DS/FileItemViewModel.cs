using Common.WPF.ViewModels;
using System.IO;
using System.Windows.Media;

namespace RomForge.ViewModels._3DS;

public class FileItemViewModel : ViewModelBase
{
    private int _progress;
    private string _status = string.Empty;
    private string _selectedTargetFormat = string.Empty;

    public int No { get; set; }

    public string FilePath { get; }

    public string FileName => Path.GetFileNameWithoutExtension(FilePath);

    public string Extension => Path.GetExtension(FilePath).TrimStart('.').ToLowerInvariant();

    public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public string FileSize { get; }

    public long FileSizeBytes { get; }

    public List<string> AvailableFormats { get; private set; } = [];

    public string SelectedTargetFormat
    {
        get => _selectedTargetFormat;
        set
        {
            if (_selectedTargetFormat != value)
            {
                _selectedTargetFormat = value;
                OnPropertyChanged();
            }
        }
    }

    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); }
    }

    public Brush StatusColor => Status switch
    {
        "완료" => Brushes.LimeGreen,
        "실패" => Brushes.Red,
        "취소" => Brushes.Gray,
        "변환중" => Brushes.DodgerBlue,
        "건너뜀" => Brushes.Gray,
        _ => Brushes.Transparent
    };

    public Brush ExtensionBackground => Extension switch
    {
        "3ds" => Brush("#FFE094"),
        "cci" => Brush("#FFCE73"),
        "cia" => Brush("#C96F2C"),
        "zcci" => Brush("#D48843"),

        _ => Brushes.Transparent
    };

    public FileItemViewModel(string filePath)
    {
        FilePath = filePath;
        FileSizeBytes = new FileInfo(filePath).Length;
        FileSize = FormatSize(FileSizeBytes);

        InitAvailableFormats();
    }

    private void InitAvailableFormats()
    {
        AvailableFormats.Clear();

        switch (Extension)
        {
            case "3ds":                
                AvailableFormats.Add("CIA");
                AvailableFormats.Add("ZCCI");
                SelectedTargetFormat = "CIA";
                break;

            case "cci":                
                AvailableFormats.Add("CIA");
                AvailableFormats.Add("ZCCI");
                SelectedTargetFormat = "CIA";
                break;

            case "cia":
                AvailableFormats.Add("CCI");
                AvailableFormats.Add("ZCCI");
                SelectedTargetFormat = "CCI";
                break;

            case "zcci":
                AvailableFormats.Add("CCI");
                SelectedTargetFormat = "CCI";
                break;

            default:
                SelectedTargetFormat = "미지원";
                break;
        }
    }

    public static string FormatSize(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes >= GB) return $"{bytes / (double)GB:N2} GB";
        if (bytes >= MB) return $"{bytes / (double)MB:N2} MB";
        if (bytes >= KB) return $"{bytes / (double)KB:N2} KB";
        return $"{bytes} Bytes";
    }

    private static SolidColorBrush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(c);

        brush.Freeze();

        return brush;
    }
}