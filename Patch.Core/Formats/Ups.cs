namespace Patch.Core.Formats;

public static class Ups
{
    private static readonly byte[] HeaderBytes = [(byte)'U', (byte)'P', (byte)'S', (byte)'1'];

    public static async Task ApplyPatchAsync(string sourcePath, string patchPath, string outputPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateInputFiles(sourcePath, patchPath);

        byte[] input = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        byte[] patch = await File.ReadAllBytesAsync(patchPath, cancellationToken);
        byte[] result = await Task.Run(() => Decode(input, patch, onProgress, cancellationToken), cancellationToken);

        await File.WriteAllBytesAsync(outputPath, result, cancellationToken);
    }

    public static async Task<byte[]> ApplyPatchAsync(byte[] sourceData, byte[] patchData, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Decode(sourceData, patchData, onProgress, cancellationToken), cancellationToken);
    }

    public static async Task CreatePatchAsync(string sourcePath, string newPath, string patchPath, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateInputFiles(sourcePath, newPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        byte[] target = await File.ReadAllBytesAsync(newPath, cancellationToken);
        byte[] patch = await Task.Run(() => Encode(source, target, onProgress, cancellationToken), cancellationToken);

        await File.WriteAllBytesAsync(patchPath, patch, cancellationToken);
    }

    private unsafe static byte[] Decode(byte[] input, byte[] patch, Action<double>? onProgress, CancellationToken cancellationToken)
    {
        if (patch.Length < 12) throw new InvalidDataException("UPS 패치 파일이 너무 짧습니다.");

        fixed (byte* pPat = patch)
        {
            if (pPat[0] != 'U' || pPat[1] != 'P' || pPat[2] != 'S' || pPat[3] != '1')
                throw new InvalidDataException("유효하지 않은 UPS 헤더입니다.");

            int pos = 4;
            long inputSize = ReadVli(pPat, ref pos, patch.Length);
            long outputSize = ReadVli(pPat, ref pos, patch.Length);
            byte[] output = new byte[outputSize];
            Buffer.BlockCopy(input, 0, output, 0, (int)Math.Min(input.Length, outputSize));

            fixed (byte* pOut = output)
            {
                long outOffset = 0;
                int patchEnd = patch.Length - 12;

                while (pos < patchEnd)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long skip = ReadVli(pPat, ref pos, patchEnd);
                    outOffset += skip;

                    while (pos < patchEnd)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        byte b = pPat[pos++];

                        if (b == 0) 
                            break;

                        if (outOffset < outputSize)
                            pOut[outOffset] ^= b;

                        outOffset++;
                    }

                    if (onProgress != null && pos % Math.Max(1, patchEnd / 100) == 0)
                        onProgress((double)pos / patchEnd);
                }
            }

            uint srcCrc = *(uint*)(pPat + patch.Length - 12);
            uint dstCrc = *(uint*)(pPat + patch.Length - 8);
            uint patCrc = *(uint*)(pPat + patch.Length - 4);

            if (CalculateCrc32(input) != srcCrc) 
                throw new InvalidDataException("Input CRC32 불일치");

            if (CalculateCrc32(output) != dstCrc)
                throw new InvalidDataException("Output CRC32 불일치");

            if (CalculateCrc32(patch, patch.Length - 4) != patCrc)
                throw new InvalidDataException("Patch CRC32 불일치");

            return output;
        }
    }

    private unsafe static byte[] Encode(byte[] source, byte[] target, Action<double>? onProgress, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();

        ms.Write(HeaderBytes, 0, 4);
        WriteVli(ms, source.Length);
        WriteVli(ms, target.Length);

        int maxLen = Math.Max(source.Length, target.Length);

        fixed (byte* pSrc = source, pTar = target)
        {
            int i = 0;
            int lastOffset = 0;

            while (i < maxLen)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte s = i < source.Length ? pSrc[i] : (byte)0;
                byte t = i < target.Length ? pTar[i] : (byte)0;

                if (s != t)
                {
                    WriteVli(ms, i - lastOffset);

                    int blockStart = i;

                    while (i < maxLen)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if ((i < source.Length ? pSrc[i] : (byte)0) == (i < target.Length ? pTar[i] : (byte)0)) 
                            break;

                        i++;
                    }

                    byte[] xorBlock = new byte[i - blockStart + 1];

                    for (int j = 0; j < i - blockStart; j++)
                    {
                        byte sb = (blockStart + j) < source.Length ? pSrc[blockStart + j] : (byte)0;
                        byte tb = (blockStart + j) < target.Length ? pTar[blockStart + j] : (byte)0;

                        xorBlock[j] = (byte)(sb ^ tb);
                    }

                    xorBlock[i - blockStart] = 0;
                    ms.Write(xorBlock, 0, xorBlock.Length);
                    lastOffset = i;
                }
                else i++;

                if (onProgress != null && i % 1000 == 0)
                    onProgress((double)i / maxLen);
            }
        }

        byte[] currentPatch = ms.ToArray();
        byte[] finalPatch = new byte[currentPatch.Length + 12];

        Buffer.BlockCopy(currentPatch, 0, finalPatch, 0, currentPatch.Length);
        BitConverter.GetBytes(CalculateCrc32(source)).CopyTo(finalPatch, finalPatch.Length - 12);
        BitConverter.GetBytes(CalculateCrc32(target)).CopyTo(finalPatch, finalPatch.Length - 8);
        BitConverter.GetBytes(CalculateCrc32(currentPatch)).CopyTo(finalPatch, finalPatch.Length - 4);

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
            if (!File.Exists(path)) 
                throw new FileNotFoundException($"파일을 찾을 수 없습니다: {path}");
    }
}