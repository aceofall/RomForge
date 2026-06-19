namespace PBP.Core.Models;

public class IsoIndex
{
    public uint Offset { get; set; }
    public uint Length { get; set; }
    public uint[] Dummy { get; set; } = new uint[6];
}