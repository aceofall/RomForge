using RomZip.Core.Enums;

namespace RomZip.Core.Models;

public class DetectResult
{
    public RomFormat Format { get; init; }
    public ConvertDirection Direction { get; init; }
    public string OutputExtension { get; init; } = string.Empty;
}