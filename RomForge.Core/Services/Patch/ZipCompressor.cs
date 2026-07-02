using Common;
using System.IO;
using System.IO.Compression;

namespace RomForge.Core.Services.Patch;

public class ZipCompressor(Action<string, LogLevel> log, IProgress<ProgressInfo> progress)
{
    public async Task CompressFromBytesAsync(byte[] patched, string sourcePath, string outputDir, CancellationToken ct)
    {
        log($"압축 시작: {Path.GetFileName(sourcePath)}", LogLevel.Highlight);

        string zipPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(sourcePath) + ".zip");

        zipPath = Utils.GetUniqueFilePath(zipPath);

        await Task.Run(() =>
        {
            using var zipStream = new FileStream(zipPath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
            var entry = archive.CreateEntry(Path.GetFileName(sourcePath));
            using var entryStream = entry.Open();
            long totalBytes = patched.Length;
            long bytesWrittenTotal = 0;
            int chunkSize = 81920;

            while (bytesWrittenTotal < totalBytes)
            {
                ct.ThrowIfCancellationRequested();

                int bytesToWrite = (int)Math.Min(chunkSize, totalBytes - bytesWrittenTotal);

                entryStream.Write(patched, (int)bytesWrittenTotal, bytesToWrite);
                bytesWrittenTotal += bytesToWrite;

                if (totalBytes > 0)
                    progress.Report(new ProgressInfo { Label = "압축 중...", Percent = (int)((double)bytesWrittenTotal / totalBytes * 100) });
            }
        }, ct);

        log($"압축 완료: {zipPath}", LogLevel.Ok);
    }

    public async Task CompressFromFileAsync(string sourcePath, string outputPath, string outputDir, CancellationToken ct)
    {
        log($"압축 시작: {Path.GetFileName(sourcePath)}", LogLevel.Highlight);

        string zipPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(sourcePath) + ".zip");
        zipPath = Utils.GetUniqueFilePath(zipPath);

        await Task.Run(() =>
        {
            using var zipStream = new FileStream(zipPath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
            var entry = archive.CreateEntry(Path.GetFileName(sourcePath));
            using var entryStream = entry.Open();
            using var sourceStream = new FileStream(outputPath, FileMode.Open, FileAccess.Read);
            byte[] buffer = new byte[81920];
            long totalBytes = sourceStream.Length;
            long bytesReadTotal = 0;
            int bytesRead;

            while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();

                entryStream.Write(buffer, 0, bytesRead);
                bytesReadTotal += bytesRead;

                if (totalBytes > 0)
                    progress.Report(new ProgressInfo { Label = "압축 중...", Percent = (int)((double)bytesReadTotal / totalBytes * 100) });
            }
        }, ct);

        File.Delete(outputPath);
        log($"압축 완료: {zipPath}", LogLevel.Ok);
    }
}