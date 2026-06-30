namespace Patch.Core.Formats;

public static class Ips
{
    private static readonly byte[] PatchHeader = [(byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H'];
    private static readonly byte[] PatchFooter = [(byte)'E', (byte)'O', (byte)'F'];

    public static async Task CreatePatchAsync(string sourcePath, string newPath, string patchPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateInputFiles(sourcePath, newPath);

        byte[] original = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        byte[] modified = await File.ReadAllBytesAsync(newPath, cancellationToken);
        byte[] patch = await Task.Run(() => Encode(original, modified, onProgress, cancellationToken), cancellationToken);

        await File.WriteAllBytesAsync(patchPath, patch, cancellationToken);
    }

    public static async Task ApplyPatchAsync(string sourcePath, string patchPath, string outputPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateInputFiles(sourcePath, patchPath);

        byte[] rom = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        byte[] ips = await File.ReadAllBytesAsync(patchPath, cancellationToken);
        byte[] result = await Task.Run(() => Decode(rom, ips, onProgress, cancellationToken), cancellationToken);

        await File.WriteAllBytesAsync(outputPath, result, cancellationToken);
    }

    public static async Task<byte[]> ApplyPatchAsync(byte[] sourceData, byte[] patchData, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Decode(sourceData, patchData, onProgress, cancellationToken), cancellationToken);
    }

    private unsafe static byte[] Encode(byte[] original, byte[] modified, Action<double>? onProgress, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        ms.Write(PatchHeader, 0, 5);

        int maxLen = Math.Max(original.Length, modified.Length);

        fixed (byte* pOrg = original, pMod = modified)
        {
            int pos = 0;

            while (pos < maxLen)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte orgByte = pos < original.Length ? pOrg[pos] : (byte)0;
                byte modByte = pos < modified.Length ? pMod[pos] : (byte)0;

                if (orgByte != modByte)
                {
                    int start = pos;

                    while (pos < maxLen && (pos - start) < 0xFFFF)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if ((pos < original.Length ? pOrg[pos] : (byte)0) == (pos < modified.Length ? pMod[pos] : (byte)0))
                            break;

                        pos++;
                    }

                    int size = pos - start;
                    byte firstMod = start < modified.Length ? pMod[start] : (byte)0;
                    bool isRle = size >= 3;

                    if (isRle)
                    {
                        for (int j = start + 1; j < start + size; j++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if ((j < modified.Length ? pMod[j] : (byte)0) != firstMod)
                            {
                                isRle = false;
                                break;
                            }
                        }
                    }

                    ms.WriteByte((byte)((start >> 16) & 0xFF));
                    ms.WriteByte((byte)((start >> 8) & 0xFF));
                    ms.WriteByte((byte)(start & 0xFF));

                    if (isRle)
                    {
                        ms.WriteByte(0);
                        ms.WriteByte(0);
                        ms.WriteByte((byte)((size >> 8) & 0xFF));
                        ms.WriteByte((byte)(size & 0xFF));
                        ms.WriteByte(firstMod);
                    }
                    else
                    {
                        ms.WriteByte((byte)((size >> 8) & 0xFF));
                        ms.WriteByte((byte)(size & 0xFF));

                        if (start < modified.Length)
                        {
                            int available = Math.Min(size, modified.Length - start);
                            ms.Write(modified, start, available);

                            for (int i = available; i < size; i++) 
                                ms.WriteByte(0);
                        }
                        else
                        {
                            for (int i = 0; i < size; i++) 
                                ms.WriteByte(0);
                        }
                    }
                }
                else pos++;

                if (onProgress != null && pos % Math.Max(1, maxLen / 100) == 0)
                    onProgress((double)pos / maxLen);
            }
        }

        ms.Write(PatchFooter, 0, 3);

        return ms.ToArray();
    }

    private unsafe static byte[] Decode(byte[] rom, byte[] ips, Action<double>? onProgress, CancellationToken cancellationToken)
    {
        if (ips.Length < 8)
            throw new InvalidDataException("IPS 파일이 너무 짧습니다.");

        fixed (byte* pIps = ips)
        {
            if (pIps[0] != 'P' || pIps[1] != 'A' || pIps[2] != 'T' || pIps[3] != 'C' || pIps[4] != 'H')
                throw new InvalidDataException("유효하지 않은 IPS 헤더입니다.");

            byte[] result = new byte[rom.Length];
            Buffer.BlockCopy(rom, 0, result, 0, rom.Length);

            int actualFinalSize = rom.Length;
            int pos = 5;

            while (pos + 3 <= ips.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (pIps[pos] == 'E' && pIps[pos + 1] == 'O' && pIps[pos + 2] == 'F')
                    break;

                int offset = (pIps[pos] << 16) | (pIps[pos + 1] << 8) | pIps[pos + 2];
                pos += 3;

                if (pos + 2 > ips.Length)
                    break;

                int size = (pIps[pos] << 8) | (pIps[pos + 1]);
                pos += 2;

                if (size == 0)
                {
                    if (pos + 3 > ips.Length)
                        break;

                    int rleCount = (pIps[pos] << 8) | pIps[pos + 1];
                    byte rleValue = pIps[pos + 2];
                    pos += 3;

                    EnsureCapacity(ref result, offset + rleCount);

                    if (offset + rleCount > actualFinalSize)
                        actualFinalSize = offset + rleCount;

                    fixed (byte* pRes = result)
                        for (int i = 0; i < rleCount; i++) pRes[offset + i] = rleValue;
                }
                else
                {
                    if (pos + size > ips.Length)
                        break;

                    EnsureCapacity(ref result, offset + size);

                    if (offset + size > actualFinalSize)
                        actualFinalSize = offset + size;

                    fixed (byte* pRes = result)
                        Buffer.MemoryCopy(pIps + pos, pRes + offset, result.Length - offset, size);

                    pos += size;
                }

                onProgress?.Invoke((double)pos / ips.Length);
            }

            if (result.Length != actualFinalSize)
                Array.Resize(ref result, actualFinalSize);

            return result;
        }
    }

    private static void EnsureCapacity(ref byte[] array, int requiredSize)
    {
        if (array.Length < requiredSize)
            Array.Resize(ref array, Math.Max(array.Length * 2, requiredSize));
    }

    private static void ValidateInputFiles(params string[] paths)
    {
        foreach (var path in paths)
            if (!File.Exists(path))
                throw new FileNotFoundException($"파일을 찾을 수 없습니다: {path}");
    }
}