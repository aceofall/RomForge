using PBP.Core.Services;

namespace PBP.Core.Models;

public class CueIndex
{
    public int Number { get; set; }

    public IndexPosition Position { get; set; } = new();
}