// WudDecompressionService.cs
//
// Thin service wrapper around WudReader (see WudReader.cs / RomForge.WiiU.Disc) for use
// from RomForge's service layer. Handles both .wud (already raw) and .wux (sector-deduplicated)
// input transparently — input that's already raw is just streamed straight through.

namespace WiiU.Core.Services;

public sealed class WudDecompressionService
{
    /// <summary>Result of a decompression run.</summary>
    public sealed record Result(bool WasCompressed, long BytesWritten);

    /// <summary>
    /// Decompresses (or copies-through, if already raw) <paramref name="inputPath"/> into
    /// <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="progress">Optional progress callback, reported as bytes-written / total-size (0..1).</param>
    public Result Decompress(string inputPath, string outputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var reader = WudReader.Open(inputPath);
        long total = reader.UncompressedSize;

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        using var outStream = File.Create(outputPath);
        long written = 0;

        reader.ReadAll((chunk, offset) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            outStream.Write(chunk.Span);
            written += chunk.Length;
            if (total > 0)
                progress?.Report((double)written / total);
        }, chunkSize: 4 * 1024 * 1024);

        return new Result(reader.IsCompressed, written);
    }

    /// <summary>Async variant — runs the (synchronous, I/O-bound) decompression on a background thread.</summary>
    public Task<Result> DecompressAsync(string inputPath, string outputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => Task.Run(() => Decompress(inputPath, outputPath, progress, cancellationToken), cancellationToken);

    /// <summary>Peeks at a .wud/.wux file's header without decompressing anything, useful for
    /// showing size/type info in a UI before committing to a full unpack.</summary>
    public (bool IsCompressed, long UncompressedSize) Inspect(string path)
    {
        using var reader = WudReader.Open(path);
        return (reader.IsCompressed, reader.UncompressedSize);
    }
}