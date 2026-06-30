namespace _3DS.Core.Save.Models;

public struct DiffInfo
{
    public int SecondaryTableOffset;
    public int PrimaryTableOffset;
    public int TableLen;
    public int PartitionOffset;
    public int PartitionLen;
    public int End;
}