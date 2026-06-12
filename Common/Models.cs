namespace Common;

public enum LogLevel { Info, Ok, Warn, Error }

public class LogEntry
{
    public required string Message { get; set; }
    public LogLevel Level { get; set; }
}

public class ProgressInfo
{
    public int Percent { get; set; }
}
