using System.Text;

namespace Patch.Core.Formats;

public static class Ppf
{
    private static readonly byte[] HeaderMagic30 = [(byte)'P', (byte)'P', (byte)'F', (byte)'3', (byte)'0'];
    private const int DescriptionOffset = 6;
    private const int DescriptionLength = 50;

    public static async Task ApplyPatchAsync(string sourcePath, string patchPath, string outputPath, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        ValidateInputFiles(sourcePath, patchPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct);
        byte[] patch = await File.ReadAllBytesAsync(patchPath, ct);
        byte[] result = await Task.Run(() => Decode(source, patch, onProgress, ct), ct);

        await File.WriteAllBytesAsync(outputPath, result, ct);
    }

    public static async Task<byte[]> ApplyPatchAsync(byte[] sourceData, byte[] patchData, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        return await Task.Run(() => Decode(sourceData, patchData, onProgress, ct), ct);
    }

    public static async Task CreatePatchAsync(string sourcePath, string newPath, string patchPath, Action<double>? onProgress = null, string description = "", bool enableBlockcheck = true, CancellationToken ct = default)
    {
        ValidateInputFiles(sourcePath, newPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct);
        byte[] target = await File.ReadAllBytesAsync(newPath, ct);
        byte[] result = await Task.Run(() => Encode(source, target, onProgress, description, enableBlockcheck, ct), ct);

        await File.WriteAllBytesAsync(patchPath, result, ct);
    }

    private unsafe static byte[] Decode(byte[] source, byte[] patch, Action<double>? onProgress, CancellationToken ct)
    {
        if (patch.Length < 56) 
            throw new InvalidDataException("PPF 파일이 너무 짧습니다.");

        fixed (byte* pPat = patch)
        {
            if (pPat[0] != 'P' || pPat[1] != 'P' || pPat[2] != 'F')
                throw new InvalidDataException("유효하지 않은 PPF 헤더입니다.");

            byte version = (byte)(pPat[3] - '0');

            if (version < 1 || version > 3) 
                throw new InvalidDataException($"지원하지 않는 PPF 버전입니다: PPF {version}.0");

            byte[] output = new byte[source.Length];

            Buffer.BlockCopy(source, 0, output, 0, source.Length);

            int dataEnd = patch.Length;
            bool isUndoAvailable = false;

            if (version >= 2 && patch.Length > 10)
            {
                int lenIdx = version == 2 ? 4 : 2;

                if (patch.Length > lenIdx + 4)
                {
                    byte* tail = pPat + patch.Length - lenIdx - 4;

                    if (tail[0] == '.' && tail[1] == 'D' && tail[2] == 'I' && tail[3] == 'Z')
                    {
                        int idLen = version == 2 ? *(int*)(pPat + patch.Length - 4) : *(ushort*)(pPat + patch.Length - 2);

                        dataEnd -= version == 2 ? idLen + 38 : idLen + 36;
                    }
                }
            }

            int pos = 0;

            if (version == 1)
                pos = 56;
            else if (version == 2)
            {
                ValidateBlockcheck(pPat, patch, source, 0, 60, 2);

                pos = 1084;
            }
            else
            {
                byte blockcheck = pPat[57];

                isUndoAvailable = pPat[58] == 1;

                byte imagetype = pPat[56];

                if (blockcheck != 0)
                {
                    ValidateBlockcheck(pPat, patch, source, imagetype, 60, 3);

                    pos = 1084;
                }
                else pos = 60;
            }

            int patchDataLen = Math.Max(1, dataEnd - pos);

            fixed (byte* pOut = output)
            {
                while (pos < dataEnd)
                {
                    ct.ThrowIfCancellationRequested();

                    long offset = (version == 3) ? (*(long*)(pPat + pos)) : (*(uint*)(pPat + pos));

                    pos += (version == 3) ? 8 : 4;

                    byte length = pPat[pos++];

                    if (length == 0 || pos + length > dataEnd)
                        break;

                    if (offset >= 0 && offset + length <= output.Length) 
                        Buffer.MemoryCopy(pPat + pos, pOut + offset, output.Length - offset, length);

                    pos += length;

                    if (version == 3 && isUndoAvailable) 
                        pos += length;

                    onProgress?.Invoke((double)(pos - (dataEnd - patchDataLen)) / patchDataLen);
                }
            }

            onProgress?.Invoke(1.0);

            return output;
        }
    }

    private unsafe static byte[] Encode(byte[] source, byte[] target, Action<double>? onProgress, string description, bool enableBlockcheck, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        byte[] header = new byte[60];

        HeaderMagic30.CopyTo(header, 0);
        header[5] = 0x02;

        byte[] descBytes = Encoding.ASCII.GetBytes(description);

        Array.Copy(descBytes, 0, header, DescriptionOffset, Math.Min(descBytes.Length, DescriptionLength));

        header[57] = (byte)(enableBlockcheck ? 1 : 0);
        ms.Write(header, 0, header.Length);

        if (enableBlockcheck)
        {
            const long blockStart = 0x9320L;

            if (source.Length < blockStart + 1024)
                throw new InvalidDataException("소스 파일이 너무 짧아 blockcheck를 생성할 수 없습니다.");

            ms.Write(source, (int)blockStart, 1024);
        }

        int maxLen = Math.Max(source.Length, target.Length);

        fixed (byte* pSrc = source, pTar = target)
        {
            int i = 0;

            while (i < maxLen)
            {
                ct.ThrowIfCancellationRequested();

                byte s = i < source.Length ? pSrc[i] : (byte)0;
                byte t = i < target.Length ? pTar[i] : (byte)0;

                if (s != t)
                {
                    int start = i;
                    byte k = 0;

                    do { k++; i++; } 
                    while (i < maxLen && (i < target.Length ? pTar[i] : (byte)0) != (i < source.Length ? pSrc[i] : (byte)0) && k != 0xff);

                    if (k == 0xff)
                        i--;

                    ms.Write(BitConverter.GetBytes((long)start), 0, 8);
                    ms.WriteByte(k);

                    int safeLen = Math.Max(0, Math.Min(k, target.Length - start));

                    if (safeLen > 0) 
                        ms.Write(target, start, safeLen);

                    if (safeLen < k) 
                        ms.Write(new byte[k - safeLen], 0, k - safeLen);
                }
                else
                    i++;

                onProgress?.Invoke((double)i / maxLen);
            }
        }

        onProgress?.Invoke(1.0);

        return ms.ToArray();
    }

    private unsafe static void ValidateBlockcheck(byte* pPat, byte[] patch, byte[] source, byte imagetype, int headerBlockOffset, int version)
    {
        if (patch.Length < headerBlockOffset + 1024) 
            throw new InvalidDataException("PPF 파일이 손상되었습니다.");

        long sourceBlockStart = imagetype != 0 ? 0x80A0L : 0x9320L;

        if (source.Length < sourceBlockStart + 1024) 
            throw new InvalidDataException("소스 파일이 너무 짧아 blockcheck를 수행할 수 없습니다.");

        fixed (byte* pSrc = source)
        {
            byte* patchBlock = pPat + headerBlockOffset;
            byte* sourceBlock = pSrc + sourceBlockStart;

            for (int i = 0; i < 1024; i++)
                if (patchBlock[i] != sourceBlock[i]) 
                    throw new InvalidDataException($"Blockcheck 실패 (PPF {version}.0)");
        }
    }

    private static void ValidateInputFiles(params string[] paths)
    {
        foreach (var path in paths) if (!File.Exists(path)) 
            throw new FileNotFoundException($"파일을 찾을 수 없습니다: {path}");
    }
}