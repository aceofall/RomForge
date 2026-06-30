using _3DS.Core.Interfaces;

namespace _3DS.Core.Services;

public class FolderRomFsFileSource(string folder, IRomFsFileSource? patchSource = null) : IRomFsFileSource
{
    public async ValueTask<Stream?> OpenFileAsync(string fullPath, CancellationToken ct = default)
    {
        if (patchSource != null)
        {
            var patchStream = await patchSource.OpenFileAsync(fullPath, ct);

            if (patchStream != null)
                return patchStream;
        }

        string localPath = Path.Combine(folder, fullPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(localPath))
            return null;

        return File.OpenRead(localPath);
    }
}