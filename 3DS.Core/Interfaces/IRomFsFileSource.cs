namespace _3DS.Core.Interfaces;

public interface IRomFsFileSource
{
    ValueTask<Stream?> OpenFileAsync(string fullPath, CancellationToken ct = default);
}