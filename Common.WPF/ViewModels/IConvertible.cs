namespace Common.WPF.ViewModels;

public interface IConvertible
{
    List<string> AvailableFormats { get; }

    string SelectedTargetFormat { get; set; }
}