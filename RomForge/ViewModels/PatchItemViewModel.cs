using Common;
using Patch.Core.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.IO;

namespace RomForge.ViewModels;

public class PatchItemViewModel(PatchPair pair, int no) : INotifyPropertyChanged
{
    private int    _progress;
    private string _status = string.Empty;

    public int No { get; set; } = no;
    public string BaseName { get; } = pair.BaseName;
    public string SourcePath { get; } = pair.SourcePath;
    public string PatchPath { get; } = pair.PatchPath;
    public PairStatus PairStatus { get; } = pair.Status;

    public string SourceFileName => string.IsNullOrEmpty(SourcePath)
        ? "-" : Path.GetFileName(SourcePath.Contains('|') ? SourcePath.Split('|')[1] : SourcePath);

    public string PatchFileName => string.IsNullOrEmpty(PatchPath)
        ? "-" : Path.GetFileName(PatchPath.Contains('|') ? PatchPath.Split('|')[1] : PatchPath);

    public string StatusLabel => PairStatus switch
    {
        PairStatus.Matched      => "매칭됨",
        PairStatus.OrphanSource => "패치없음",
        PairStatus.OrphanPatch  => "원본없음",
        _                       => string.Empty
    };

    public Brush StatusLabelColor => PairStatus switch
    {
        PairStatus.Matched      => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
        PairStatus.OrphanSource => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
        PairStatus.OrphanPatch  => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
        _                       => Brushes.Gray
    };

    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(RunStatusColor)); }
    }

    public Brush RunStatusColor => Status switch
    {
        "완료"   => Brushes.LimeGreen,
        "실패"   => Brushes.Red,
        "취소"   => Brushes.Gray,
        "패치중" => Brushes.DodgerBlue,
        _        => Brushes.Transparent
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
