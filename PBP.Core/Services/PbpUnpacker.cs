namespace PBP.Core.Services;

public class PbpUnpacker
{
    public Action<uint>? OnProgress { get; set; }

    public Action<string>? OnNotify { get; set; }

    public void Unpack(string pbpPath, string outputDir, bool createCuesheet = true, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(pbpPath, FileMode.Open, FileAccess.Read);
        var reader = new PbpReader(stream);

        Directory.CreateDirectory(outputDir);

        var baseName = Path.GetFileNameWithoutExtension(pbpPath);
        var isMultiDisc = reader.Discs.Count > 1;

        foreach (var disc in reader.Discs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            disc.ProgressEvent = OnProgress;

            var discSuffix = isMultiDisc ? $" (Disc {disc.Index})" : string.Empty;
            var binPath = Path.Combine(outputDir, $"{baseName}{discSuffix}.bin");
            var cuePath = Path.Combine(outputDir, $"{baseName}{discSuffix}.cue");

            OnNotify?.Invoke($"Writing {binPath}...");

            using (var binStream = new FileStream(binPath, FileMode.Create, FileAccess.Write))
                disc.CopyTo(binStream, cancellationToken);

            if (createCuesheet)
            {
                var cueFile = TOCHelper.TOCtoCUE(disc.TOC, Path.GetFileName(binPath));

                CueFileWriter.Write(cueFile, cuePath);
                OnNotify?.Invoke($"Writing {cuePath}...");
            }
        }

        OnNotify?.Invoke("Extract complete!");
    }
}