using Common;

namespace RomForge.Models;

public class LogEntry
{
    public required string Message { get; set; }

    public LogLevel Level { get; set; }
}