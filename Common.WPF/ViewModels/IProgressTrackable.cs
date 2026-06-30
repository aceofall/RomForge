namespace Common.WPF.ViewModels;

public interface IProgressTrackable
{
    int No { get; set; }

    int Progress { get; set; }

    string Status { get; set; }
}