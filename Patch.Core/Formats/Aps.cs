namespace Patch.Core.Formats;

public static class Aps
{
    private static readonly byte[] HeaderBytes = [(byte)'A', (byte)'P', (byte)'S', (byte)'1'];

    public static async Task ApplyPatchAsync(string sourcePath, string patchPath, string outputPath, Action<double>? onProgress = null, CancellationToken cancellation = default)
    {
        ValidateInputFiles(sourcePath, patchPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, cancellation);
        byte[] patch = await File.ReadAllBytesAsync(patchPath, cancellation);
        byte[] result = await Task.Run(() => Decode(source, patch, onProgress, cancellation), cancellation);

        await File.WriteAllBytesAsync(outputPath, result, cancellation);
    }

    public static async Task<byte[]> ApplyPatchAsync(byte[] sourceData, byte[] patchData, Action<double>? onProgress = null, CancellationToken cancellation = default)
    {
        return await Task.Run(() => Decode(sourceData, patchData, onProgress, cancellation), cancellation);
    }

    public static async Task CreatePatchAsync(string sourcePath, string newPath, string patchPath, Action<double>? onProgress = null, CancellationToken cancellation = default)
    {
        ValidateInputFiles(sourcePath, newPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, cancellation);
        byte[] target = await File.ReadAllBytesAsync(newPath, cancellation);
        byte[] result = await Task.Run(() => Encode(source, target, onProgress, cancellation), cancellation);

        await File.WriteAllBytesAsync(patchPath, result, cancellation);
    }

    private unsafe static byte[] Decode(byte[] source, byte[] patch, Action<double>? onProgress, CancellationToken cancellation)
    {
        if (patch.Length < 8) throw new InvalidDataException("APS 패치 파일이 너무 짧습니다.");

        fixed (byte* pPat = patch)
        {
            if (pPat[0] != 'A' || pPat[1] != 'P' || pPat[2] != 'S' || pPat[3] != '1')
                throw new InvalidDataException("유효하지 않은 APS 헤더입니다.");

            byte[] output = new byte[source.Length];
            Buffer.BlockCopy(source, 0, output, 0, source.Length);

            int pos = 4;

            fixed (byte* pOut = output)
            {
                while (pos + 5 <= patch.Length)
                {
                    cancellation.ThrowIfCancellationRequested();

                    uint offset = *(uint*)(pPat + pos);

                    pos += 4;

                    byte length = pPat[pos++];

                    if (length == 0 || pos + length > patch.Length)
                        break;

                    if (offset + length <= output.Length)
                        Buffer.MemoryCopy(pPat + pos, pOut + offset, output.Length - offset, length);

                    pos += length;

                    if (onProgress != null && pos % Math.Max(1, patch.Length / 100) == 0)
                        onProgress((double)pos / patch.Length);
                }
            }
            return output;
        }
    }

    private unsafe static byte[] Encode(byte[] source, byte[] target, Action<double>? onProgress, CancellationToken cancellation)
    {
        using var ms = new MemoryStream();

        ms.Write(HeaderBytes, 0, 4);

        int maxLen = Math.Max(source.Length, target.Length);

        fixed (byte* pSrc = source, pTar = target)
        {
            int i = 0;

            while (i < maxLen)
            {
                cancellation.ThrowIfCancellationRequested();

                byte s = i < source.Length ? pSrc[i] : (byte)0;
                byte t = i < target.Length ? pTar[i] : (byte)0;

                if (s != t)
                {
                    int start = i;

                    while (i < maxLen && (i - start) < 255)
                    {
                        byte sb = i < source.Length ? pSrc[i] : (byte)0;
                        byte tb = i < target.Length ? pTar[i] : (byte)0;

                        if (sb == tb) 
                            break;

                        i++;
                    }

                    int length = i - start;

                    ms.Write(BitConverter.GetBytes((uint)start), 0, 4);
                    ms.WriteByte((byte)length);
                    ms.Write(target, start, length);
                }
                else i++;

                if (onProgress != null && i % 1000 == 0)
                    onProgress((double)i / maxLen);
            }
        }
        return ms.ToArray();
    }

    private static void ValidateInputFiles(params string[] paths)
    {
        foreach (var path in paths)
            if (!File.Exists(path))
                throw new FileNotFoundException($"파일을 찾을 수 없습니다: {path}");
    }
}