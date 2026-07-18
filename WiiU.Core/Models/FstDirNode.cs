namespace WiiU.Core.Models;

public sealed class FstDirNode
{
    public readonly SortedDictionary<string, FstDirNode> Dirs = new(StringComparer.Ordinal);
    public readonly List<(string Name, int ClusterIndex, uint OffsetField, uint SizeField)> Files = [];
}