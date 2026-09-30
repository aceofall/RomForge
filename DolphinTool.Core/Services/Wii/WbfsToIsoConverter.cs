namespace DolphinTool.Core.Services.Wii;

public static class WbfsToIsoConverter
{
    private const int ChunkSize = 1 << 20;

    public static void Convert(string inputPath, string outputPath, Action<double>? progress = null, CancellationToken ct = default)
    {
        bool succeeded = false;

        try
        {
            using var input = RvzInputSource.Open(inputPath);

            if (input is not WbfsSource)
                throw new InvalidDataException("WBFS 파일이 아닙니다.");

            long length = input.Length;

            using var output = File.OpenHandle(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, length);

            RandomAccess.SetLength(output, length);

            byte[] buffer = new byte[ChunkSize];
            long offset = 0;

            while (offset < length)
            {
                ct.ThrowIfCancellationRequested();

                int size = (int)Math.Min(ChunkSize, length - offset);
                var span = buffer.AsSpan(0, size);

                input.Read(offset, span);

                if (span.ContainsAnyExcept((byte)0))
                    RandomAccess.Write(output, span, offset);

                offset += size;

                progress?.Invoke(Math.Min(1.0, (double)offset / length));
            }

            succeeded = true;
        }
        finally
        {
            if (!succeeded)
            {
                try
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                }
                catch { }
            }
        }
    }
}