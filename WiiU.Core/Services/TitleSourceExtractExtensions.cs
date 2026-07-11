namespace WiiU.Core.Services;

public static class TitleSourceExtractExtensions
{
    private const int BufferSize = 1024 * 1024;

    public static void ExtractTo(this ITitleSource source, string destinationFolder, Action<int, int, string>? onFileProgress = null, CancellationToken cancellationToken = default)
    {
        var paths = source.EnumerateFiles().ToList();
        int total = paths.Count;
        int done = 0;
        var buffer = new byte[BufferSize];

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string destPath = Path.Combine(destinationFolder, path.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using (var outStream = File.Create(destPath))
            using (var inStream = source.OpenRead(path))
            {
                int read;
                while ((read = inStream.Read(buffer, 0, buffer.Length)) > 0)
                    outStream.Write(buffer, 0, read);
            }

            done++;
            onFileProgress?.Invoke(done, total, path);
        }
    }
}