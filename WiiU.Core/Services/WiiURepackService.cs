using WiiU.Core.Models;

namespace WiiU.Core.Services;

public sealed class WiiURepackService
{
    private const int BufferSize = 1024 * 1024;

    public static void Repack(ITitleSource source, string outputWuaPath, string? patchFolder = null, string? titleIdHexOverride = null, int? titleVersionOverride = null, Action<int, int, string>? onFileProgress = null, CancellationToken ct = default)
    {
        RepackMultiple([new RepackEntry(source, patchFolder, titleIdHexOverride, titleVersionOverride)], outputWuaPath, onFileProgress, ct);
    }

    public static void RepackMultiple(IReadOnlyList<RepackEntry> entries, string outputWuaPath, Action<int, int, string>? onFileProgress = null, CancellationToken ct = default)
    {
        if (entries.Count == 0)
            throw new ArgumentException("At least one entry is required.", nameof(entries));

        var resolved = new List<(string TitleFolder, ITitleSource Source, Dictionary<string, string> PatchFiles, List<string> Paths)>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            string titleIdHex = entry.TitleIdHexOverride ?? entry.Source.TitleIdHex;
            int titleVersion = entry.TitleVersionOverride ?? entry.Source.TitleVersion;
            string titleFolder = $"{titleIdHex}_v{titleVersion}";

            if (!seenFolders.Add(titleFolder))
                throw new InvalidOperationException($"Two entries both resolved to the folder name \"{titleFolder}\" — they'd collide in the output .wua. " + "Check that base/update/DLC don't share the same title ID + version.");

            var patchFiles = new Dictionary<string, string>(StringComparer.Ordinal);

            if (entry.PatchFolder is not null)
            {
                foreach (string file in Directory.EnumerateFiles(entry.PatchFolder, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(entry.PatchFolder, file).Replace(Path.DirectorySeparatorChar, '/');
                    patchFiles[relative] = file;
                }
            }

            var paths = new SortedSet<string>(entry.Source.EnumerateFiles(), StringComparer.Ordinal);

            foreach (var p in patchFiles.Keys) 
                paths.Add(p);

            resolved.Add((titleFolder, entry.Source, patchFiles, new List<string>(paths)));
        }

        int total = 0;

        foreach (var (TitleFolder, Source, PatchFiles, Paths) in resolved) 
            total += Paths.Count;

        int done = 0;
        using var outStream = File.Create(outputWuaPath);
        using var writer = new WuaWriter(outStream);
        var buffer = new byte[BufferSize];

        foreach (var (titleFolder, source, patchFiles, paths) in resolved)
        {
            writer.MakeDir(titleFolder, recursive: true);

            var writtenDirs = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in paths)
            {
                ct.ThrowIfCancellationRequested();

                EnsureDirWritten(writer, titleFolder, GetDirectoryPart(path), writtenDirs);
                writer.StartNewFile($"{titleFolder}/{path}");

                using (Stream srcStream = patchFiles.TryGetValue(path, out var patchFilePath) ? File.OpenRead(patchFilePath) : source.OpenRead(path))
                {
                    int read;

                    while ((read = srcStream.Read(buffer, 0, buffer.Length)) > 0)
                        writer.AppendData(buffer.AsSpan(0, read));
                }

                done++;

                onFileProgress?.Invoke(done, total, $"{titleFolder}/{path}");
            }
        }

        writer.FinalizeArchive();
    }

    private static string GetDirectoryPart(string path)
    {
        int idx = path.LastIndexOf('/');

        return idx < 0 ? "" : path[..idx];
    }

    private static void EnsureDirWritten(WuaWriter writer, string titleFolderName, string dirPath, HashSet<string> writtenDirs)
    {
        if (dirPath.Length == 0 || !writtenDirs.Add(dirPath)) 
            return;

        EnsureDirWritten(writer, titleFolderName, GetDirectoryPart(dirPath), writtenDirs);
        writer.MakeDir($"{titleFolderName}/{dirPath}", recursive: true);
    }
}