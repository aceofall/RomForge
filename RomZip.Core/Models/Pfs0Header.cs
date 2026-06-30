using System.Runtime.InteropServices;

namespace RomZip.Core.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Pfs0Header
{
    public uint Magic;
    public uint NumFiles;
    public uint StringTableSize;
    public uint Reserved;
}