namespace PBP.Core.Services;

public class PbpUnpacker
{
    public Action<int>? OnProgress { get; set; }

    public Action<string>? OnNotify { get; set; }

    public async Task UnpackAsync(string pbpPath, string outputDir, bool createCuesheet = true, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(pbpPath, FileMode.Open, FileAccess.Read);
        var reader = new PbpReader(stream);

        Directory.CreateDirectory(outputDir);

        var baseName = Path.GetFileNameWithoutExtension(pbpPath);
        var isMultiDisc = reader.Discs.Count > 1;
        var totalSize = reader.Discs.Sum(d => d.IsoSize);
        long baseSize = 0;

        var createdFiles = new List<string>();

        try
        {
            foreach (var disc in reader.Discs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var capturedBase = baseSize;

                disc.ProgressEvent = bytes => OnProgress?.Invoke((int)Math.Round((capturedBase + bytes) * 100.0 / totalSize));

                var discSuffix = isMultiDisc ? $" (Disc {disc.Index})" : string.Empty;
                var binPath = Path.Combine(outputDir, $"{baseName}{discSuffix}.bin");
                var cuePath = Path.Combine(outputDir, $"{baseName}{discSuffix}.cue");

                OnNotify?.Invoke($"언팩중 {binPath}...");
                createdFiles.Add(binPath);

                await Task.Run(() =>
                {
                    using var binStream = new FileStream(binPath, FileMode.Create, FileAccess.Write);

                    disc.CopyTo(binStream, cancellationToken);
                }, cancellationToken);

                if (createCuesheet)
                {
                    var cueFile = TocHelper.TOCtoCUE(disc.TOC, Path.GetFileName(binPath));

                    createdFiles.Add(cuePath); // 👈
                    CueFileWriter.Write(cueFile, cuePath);
                    OnNotify?.Invoke($"언팩중 {cuePath}...");
                }

                baseSize += disc.IsoSize;
            }
        }
        catch (Exception)
        {
            OnNotify?.Invoke("실패 - 생성된 파일 정리 중...");

            foreach (var path in createdFiles)
                try { File.Delete(path); } catch { }

            throw;
        }

        OnNotify?.Invoke("언팩 완료");
    }
}