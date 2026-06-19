using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace PBP.Core.Services;

public static class Compression
{
    public static int Compress(byte[] inbuf, byte[] outbuf, int level)
    {
        using var ms = new MemoryStream(outbuf);
        var deflater = new Deflater(level, true);
        using var outStream = new DeflaterOutputStream(ms, deflater);

        outStream.Write(inbuf, 0, inbuf.Length);
        outStream.Flush();
        outStream.Finish();

        return (int)ms.Position;
    }
}