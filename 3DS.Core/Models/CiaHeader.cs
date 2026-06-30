namespace _3DS.Core.Models;

public record struct CiaHeader
{
    public uint ArchiveHeaderSize { get; set; }
    public ushort Type { get; set; }
    public ushort Version { get; set; }
    public uint CertChainSize { get; set; }
    public uint TicketSize { get; set; }
    public uint TmdSize { get; set; }
    public uint MetaSize { get; set; }
    public ulong ContentSize { get; set; }
}