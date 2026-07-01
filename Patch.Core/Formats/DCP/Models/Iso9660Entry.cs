namespace Patch.Core.Formats.DCP.Models;

public class Iso9660Entry
{
    public string Name { get; init; } = string.Empty;

    public uint Lba { get; init; }

    public uint Size { get; init; }

    public bool IsDirectory { get; init; }

    public List<Iso9660Entry> Children { get; } = [];

    public string FullPath { get; set; } = string.Empty;
}