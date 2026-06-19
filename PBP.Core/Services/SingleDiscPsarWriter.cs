namespace PBP.Core.Services;

public static class SingleDiscPsarWriter
{
    public static void WritePsar(Stream outputStream, DiscWriteInfo disc, uint psarOffset, int compressionLevel, CancellationToken cancellationToken)
        => PsarDiscWriter.WriteDisc(outputStream, disc.IsoStream, disc.IsoLength, disc.GameId, disc.GameTitle, disc.TocData, psarOffset, false, compressionLevel, cancellationToken);
}