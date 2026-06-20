namespace PBP.Core.Models;

public class CueFile
{
    public string Path { get; set; } = string.Empty;

    public List<CueFileEntry> FileEntries { get; set; } = [];
}