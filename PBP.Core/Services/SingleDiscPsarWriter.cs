namespace PBP.Core.Services;

public static class SingleDiscPsarWriter
{
    public static void WritePsar(Stream outputStream, DiscWriteInfo disc, uint psarOffset, int compressionLevel, CancellationToken cancellationToken, Action<long, long>? onProgress = null)
        => PsarDiscWriter.WriteDisc(outputStream, disc.IsoStream, disc.IsoLength, disc.GameId, disc.GameTitle, disc.TocData, psarOffset, false, compressionLevel, cancellationToken, onProgress);
}