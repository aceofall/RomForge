using _3DS.Core.Models;

namespace _3DS.Core.Interfaces;

public interface IInstallSource : INcsdSource
{
    TmdHeader TmdHeader { get; }

    byte[] TmdRaw { get; }

    ValueTask<(Stream stream, long size)> OpenContentNcchEncrypted(int contentIndex);
}