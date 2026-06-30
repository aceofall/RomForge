namespace NSW.M1.Core.Models;

public class ProgressContext(long totalSize)
{
    public long TotalSize { get; } = totalSize;

    public long CurrentRead { get; set; }

    public DateTime LastReport { get; set; } = DateTime.MinValue;

}