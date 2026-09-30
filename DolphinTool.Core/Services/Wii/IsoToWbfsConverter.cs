using System.Buffers.Binary;

namespace DolphinTool.Core.Services.Wii;

public static class IsoToWbfsConverter
{
    private const int HdSectorShift = 9;
    private const int WbfsSectorShift = 21;
    private const int HdSectorSize = 1 << HdSectorShift;
    private const int WbfsSectorSize = 1 << WbfsSectorShift;
    private const int DiscHeaderSize = 256;
    private const long WiiSectorCount = 143432 * 2;
    private const int BlocksPerDisc = (int)(WiiSectorCount >> (WbfsSectorShift - 15));

    public static void Convert(string inputPath, string outputPath, Action<double>? progress = null, CancellationToken ct = default)
    {
        bool succeeded = false;

        try
        {
            using var input = RvzInputSource.Open(inputPath);

            Span<byte> header = stackalloc byte[0x20];

            if (input is WbfsSource || input.Length < DiscHeaderSize)
                throw new InvalidDataException("Wii ISO 파일이 아닙니다.");

            input.Read(0, header);

            if (!RvzWiiWriter.IsWii(header))
                throw new InvalidDataException("Wii ISO 파일이 아닙니다. WBFS는 Wii 디스크만 지원합니다.");

            long length = input.Length;
            long blockCount = (length + WbfsSectorSize - 1) / WbfsSectorSize;

            if (blockCount > BlocksPerDisc)
                throw new InvalidDataException("Wii 디스크 최대 크기를 초과했습니다.");

            using var output = File.OpenHandle(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None);

            var wlbaTable = new ushort[BlocksPerDisc];
            byte[] buffer = new byte[WbfsSectorSize];
            byte[] discHeader = new byte[DiscHeaderSize];
            int usedBlocks = 0;

            for (long block = 0; block < blockCount; block++)
            {
                ct.ThrowIfCancellationRequested();

                long offset = block * WbfsSectorSize;
                int size = (int)Math.Min(WbfsSectorSize, length - offset);

                input.Read(offset, buffer.AsSpan(0, size));

                if (size < WbfsSectorSize)
                    buffer.AsSpan(size).Clear();

                if (block == 0)
                    buffer.AsSpan(0, DiscHeaderSize).CopyTo(discHeader);

                if (buffer.AsSpan().ContainsAnyExcept((byte)0))
                {
                    usedBlocks++;
                    wlbaTable[block] = checked((ushort)usedBlocks);

                    RandomAccess.Write(output, buffer, (long)usedBlocks * WbfsSectorSize);
                }

                progress?.Invoke(Math.Min(1.0, (double)(block + 1) / blockCount) * 0.99);
            }

            if (usedBlocks == 0)
                throw new InvalidDataException("변환할 데이터가 없습니다.");

            long totalSize = (long)(usedBlocks + 1) * WbfsSectorSize;

            RandomAccess.SetLength(output, totalSize);

            byte[] head = new byte[HdSectorSize];

            head[0] = (byte)'W';
            head[1] = (byte)'B';
            head[2] = (byte)'F';
            head[3] = (byte)'S';

            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(4), (uint)(totalSize >> HdSectorShift));

            head[8] = HdSectorShift;
            head[9] = WbfsSectorShift;
            head[10] = 1;
            head[12] = 1;

            RandomAccess.Write(output, head, 0);

            int discInfoSize = (DiscHeaderSize + BlocksPerDisc * 2 + HdSectorSize - 1) / HdSectorSize * HdSectorSize;
            byte[] discInfo = new byte[discInfoSize];

            discHeader.CopyTo(discInfo, 0);

            for (int i = 0; i < BlocksPerDisc; i++)
                BinaryPrimitives.WriteUInt16BigEndian(discInfo.AsSpan(DiscHeaderSize + i * 2), wlbaTable[i]);

            RandomAccess.Write(output, discInfo, HdSectorSize);

            progress?.Invoke(1.0);

            succeeded = true;
        }
        finally
        {
            if (!succeeded)
            {
                try
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                }
                catch { }
            }
        }
    }
}