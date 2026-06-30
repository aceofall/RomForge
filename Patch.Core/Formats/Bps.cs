using System.Runtime.InteropServices;

namespace Patch.Core.Formats;

public static class Bps
{
    private static readonly byte[] HeaderBytes = [(byte)'B', (byte)'P', (byte)'S', (byte)'1'];

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
        if (patch.Length < 12)
            throw new InvalidDataException("BPS 패치 파일이 너무 짧습니다.");

        fixed (byte* pSrc = source, pPat = patch)
        {
            if (pPat[0] != 'B' || pPat[1] != 'P' || pPat[2] != 'S' || pPat[3] != '1')
                throw new InvalidDataException("유효하지 않은 BPS 헤더입니다.");

            int pos = 4;
            long sourceSize = ReadVli(pPat, ref pos, patch.Length);
            long targetSize = ReadVli(pPat, ref pos, patch.Length);
            long metadataSize = ReadVli(pPat, ref pos, patch.Length);

            pos += (int)metadataSize;

            byte[] target = new byte[targetSize];
            int patchEnd = patch.Length - 12;

            fixed (byte* pTar = target)
            {
                int outOffset = 0;
                int srcRelOffset = 0;
                int tarRelOffset = 0;

                while (pos < patchEnd)
                {
                    cancellation.ThrowIfCancellationRequested();

                    long data = ReadVli(pPat, ref pos, patchEnd);
                    int command = (int)(data & 3);
                    int length = (int)((data >> 2) + 1);

                    switch (command)
                    {
                        case 0:
                            Buffer.MemoryCopy(pSrc + outOffset, pTar + outOffset, targetSize - outOffset, length);
                            outOffset += length;

                            break;
                        case 1:
                            Buffer.MemoryCopy(pPat + pos, pTar + outOffset, targetSize - outOffset, length);
                            pos += length;
                            outOffset += length;

                            break;
                        case 2:
                            long od2 = ReadVli(pPat, ref pos, patchEnd);

                            srcRelOffset += (od2 & 1) == 1 ? -(int)(od2 >> 1) : (int)(od2 >> 1);
                            Buffer.MemoryCopy(pSrc + srcRelOffset, pTar + outOffset, targetSize - outOffset, length);
                            outOffset += length;
                            srcRelOffset += length;

                            break;
                        case 3:
                            long od3 = ReadVli(pPat, ref pos, patchEnd);

                            tarRelOffset += (od3 & 1) == 1 ? -(int)(od3 >> 1) : (int)(od3 >> 1);

                            for (int i = 0; i < length; i++) 
                                pTar[outOffset + i] = pTar[tarRelOffset + i];

                            outOffset += length;
                            tarRelOffset += length;

                            break;
                    }

                    if (onProgress != null && pos % Math.Max(1, patchEnd / 100) == 0)
                        onProgress((double)pos / patchEnd);
                }
            }

            uint sourceCrc = *(uint*)(pPat + patch.Length - 12);
            uint targetCrc = *(uint*)(pPat + patch.Length - 8);
            uint patchCrc = *(uint*)(pPat + patch.Length - 4);

            if (CalculateCrc32(source) != sourceCrc) 
                throw new InvalidDataException("Source CRC32 불일치");

            if (CalculateCrc32(target) != targetCrc)
                throw new InvalidDataException("Target CRC32 불일치");

            if (CalculateCrc32(patch, patch.Length - 4) != patchCrc)
                throw new InvalidDataException("Patch CRC32 불일치");

            return target;
        }
    }

    private unsafe static byte[] Encode(byte[] source, byte[] target, Action<double>? onProgress, CancellationToken cancellation)
    {
        using var ms = new MemoryStream();

        ms.Write(HeaderBytes, 0, 4);
        WriteVli(ms, source.Length);
        WriteVli(ms, target.Length);
        WriteVli(ms, 0);

        fixed (byte* pSrc = source, pTar = target)
        {
            int outOffset = 0;

            while (outOffset < target.Length)
            {
                cancellation.ThrowIfCancellationRequested();

                if (outOffset < source.Length && pSrc[outOffset] == pTar[outOffset])
                {
                    int len = 0;

                    while (outOffset + len < source.Length && outOffset + len < target.Length && pSrc[outOffset + len] == pTar[outOffset + len])
                        len++;

                    WriteVli(ms, (long)((len - 1) << 2) | 0);

                    outOffset += len;

                    if (onProgress != null && outOffset % 1000 == 0) 
                        onProgress((double)outOffset / target.Length);

                    continue;
                }

                int runLen = 0;

                while (outOffset + runLen < target.Length && runLen < 0xFFFF)
                {
                    cancellation.ThrowIfCancellationRequested();

                    if (outOffset + runLen < source.Length && pSrc[outOffset + runLen] == pTar[outOffset + runLen]) 
                        break;

                    runLen++;
                }

                if (runLen == 0) 
                    runLen = 1;

                WriteVli(ms, (long)((runLen - 1) << 2) | 1);
                ms.Write(target, outOffset, runLen);
                outOffset += runLen;

                if (onProgress != null && outOffset % 1000 == 0) 
                    onProgress((double)outOffset / target.Length);
            }
        }

        byte[] withoutPatchCrc = ms.ToArray();
        uint pCrc = CalculateCrc32(withoutPatchCrc);
        byte[] finalPatch = new byte[withoutPatchCrc.Length + 4];

        Buffer.BlockCopy(withoutPatchCrc, 0, finalPatch, 0, withoutPatchCrc.Length);
        BitConverter.GetBytes(pCrc).CopyTo(finalPatch, finalPatch.Length - 4);

        return finalPatch;
    }

    private unsafe static long ReadVli(byte* patch, ref int pos, int maxLen)
    {
        long value = 0, shift = 1;

        while (pos < maxLen)
        {
            byte b = patch[pos++];

            value += (b & 0x7f) * shift;

            if ((b & 0x80) != 0) 
                break;

            shift <<= 7;
            value += shift;
        }

        return value;
    }

    private static void WriteVli(Stream s, long value)
    {
        while (true)
        {
            byte b = (byte)(value & 0x7f);

            value >>= 7;

            if (value == 0) 
            { 
                s.WriteByte((byte)(b | 0x80));
                break;
            }

            s.WriteByte(b);
            value--;
        }
    }

    private unsafe static uint CalculateCrc32(byte[] data, int length = -1)
    {
        if (length == -1) 
            length = data.Length;

        uint crc = 0xFFFFFFFF;

        fixed (byte* p = data)
            for (int i = 0; i < length; i++)
            {
                crc ^= p[i];

                for (int j = 0; j < 8; j++) 
                    crc = (crc >> 1) ^ ((crc & 1) * 0xEDB88320);
            }

        return ~crc;
    }

    private static void ValidateInputFiles(params string[] paths)
    {
        foreach (var path in paths)
            if (!File.Exists(path)) throw new FileNotFoundException($"파일을 찾을 수 없습니다: {path}");
    }
}