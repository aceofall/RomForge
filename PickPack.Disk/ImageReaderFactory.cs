using Ionic.Zlib;

namespace PickPack.Disk
{
    public static class ImageReaderFactory
    {
        public static IImageReaderHandler? GetHandler(string extension, long maxSegmentSize, CompressionLevel compressionLevel,
            Action<long, long, string> rawProgressReporter, CancellationToken cancellationToken)
        {
            return extension.ToLowerInvariant() switch
            {
                ".zip" => new ZipReadHandler(maxSegmentSize, compressionLevel, rawProgressReporter, cancellationToken),
                ".img" => new RawImageReadHandler(rawProgressReporter),
                _ => null
            };
        }

        public static bool IsSupported(string extension) => extension.ToLowerInvariant() is ".zip" or ".img";

        public static string[] GetSupportedExtensions() => [".zip", ".img"];
    }
}