using _3DS.Core.Save.Interfaces;
using _3DS.Core.Save.Models;

namespace _3DS.Core.Save;

public class FsMeta(MetaTable dirs, MetaTable files)
{
    public readonly MetaTable Dirs = dirs;
    public readonly MetaTable Files = files;

    public MetaStat Stat() => new()
    {
        Dirs = Dirs.Stat(),
        Files = Files.Stat(),
    };
}