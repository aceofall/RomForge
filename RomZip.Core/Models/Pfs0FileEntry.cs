using System.Runtime.InteropServices;

namespace RomZip.Core.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Pfs0FileEntry
{
    public ulong Offset;
    public ulong Size;
    public uint StringTableOffset;
    public uint Reserved;
}