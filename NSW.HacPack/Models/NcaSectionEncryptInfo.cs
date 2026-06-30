namespace NSW.HacPack.Models;

public class NcaSectionEncryptInfo
{
    public ulong StartOffset;
    public ulong SectionSize;
    public byte[] SectionCtr = new byte[0x8];
    public byte[] EncKey = new byte[0x10];
}