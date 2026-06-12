using Common;

namespace RomForge.ViewModels;

public class LogEntry
{
    public required string Message { get; set; }

    public LogLevel Level { get; set; }
}