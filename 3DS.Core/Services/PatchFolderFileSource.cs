using _3DS.Core.Interfaces;

namespace _3DS.Core.Services;

public class PatchFolderFileSource(string patchFolder) : IRomFsFileSource
{
    public ValueTask<Stream?> OpenFileAsync(string fullPath, CancellationToken ct = default)
    {
        string localPath = Path.Combine(patchFolder, fullPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(localPath))
            return ValueTask.FromResult<Stream?>(null);

        return ValueTask.FromResult<Stream?>(File.OpenRead(localPath));
    }
}