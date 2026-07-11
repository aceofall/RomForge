namespace WiiU.Core.Models;

public sealed class WiiUTitleInfo
{
    public string TitleIdHex { get; init; } = "0000000000000000";

    public int TitleVersion { get; init; }

    public int FileCount { get; init; }
}