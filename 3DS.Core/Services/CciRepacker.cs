using _3DS.Core.Crypto;
using _3DS.Core.Models;
using Common;
using System.Buffers.Binary;
using System.Diagnostics;

namespace _3DS.Core.Services;

/// <summary>
/// CCI 전체 언팩 → 재패킹 파이프라인
/// seekable 출력 스트림에 직접 기록, 임시 파일 없음
/// </summary>
public static class CciRepacker
{
    private const int MediaUnit = 0x200;
    private const int NcsdHdrSize = 0x4000;

    public static async Task RepackAsync(
        string inputPath,
        string outputPath,
        KeyStore keyStore,
        IProgress<ProgressInfo>? progress = null,
        Action<string, LogLevel, string>? log = null,
        CancellationToken ct = default)
    {
        //await using var cciSource = await CciSource.OpenAsync(inputPath, keyStore, log, ct);
        //await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        //// NCSD 헤더 자리 예약 (0x4000 bytes)
        //outputStream.Write(new byte[NcsdHdrSize]);

        //var repackedPartitions = new List<(int index, long startOffset, long ncchSize, NcchHeader ncchHeader)>();

        //foreach (var content in cciSource.Contents.OrderBy(c => c.ContentIndex))
        //{
        //    ct.ThrowIfCancellationRequested();

        //    int idx = content.ContentIndex;
        //    if (idx > 7) continue;

        //    var (ncchStream, _) = cciSource.OpenContentDecrypted(idx);
        //    await using (ncchStream)
        //    {
        //        // NCCH 헤더 읽기
        //        byte[] hdrBuf = new byte[NcchHeader.Size];
        //        await ncchStream.ReadExactlyAsync(hdrBuf, ct);
        //        var ncchHeader = NcchHeader.Parse(hdrBuf);
        //        ncchStream.Position = 0;

        //        // 언팩
        //        var unpack = await NcchUnpacker.UnpackAsync(ncchStream, ncchHeader, ct);

        //        // NCCH 시작 위치 (0x200 align)
        //        long ncchStart = AlignUp(outputStream.Position, MediaUnit);
        //        outputStream.Position = ncchStart;

        //        log?.Invoke($"파티션 {idx} 재패킹 시작 @ 0x{ncchStart:X}", LogLevel.Info, string.Empty);

        //        // NCCH 빌드 → 출력 스트림에 직접 씀
        //        long ncchSize = await NcchBuilder.BuildAsync(unpack, outputStream, ct);

        //        log?.Invoke($"파티션 {idx} 완료: size=0x{ncchSize:X}", LogLevel.Ok, string.Empty);

        //        // 재패킹된 NCCH 헤더 읽기 (NCSD 헤더 작성용)
        //        byte[] repackedHdrBuf = new byte[NcchHeader.Size];
        //        outputStream.Position = ncchStart;
        //        await outputStream.ReadExactlyAsync(repackedHdrBuf, ct);
        //        var repackedHeader = NcchHeader.Parse(repackedHdrBuf);

        //        repackedPartitions.Add((idx, ncchStart, ncchSize, repackedHeader));
        //    }
        //}

        //// NCSD 헤더 작성
        //WriteNcsdHeader(outputStream, repackedPartitions);

        //await outputStream.FlushAsync(ct);
        //log?.Invoke($"완료: {outputPath}", LogLevel.Highlight, string.Empty);
    }

    private static void WriteNcsdHeader(
        Stream output,
        List<(int index, long startOffset, long ncchSize, NcchHeader ncchHeader)> partitions)
    {
        var partitionMap = new (uint offset, uint size)[8];

        foreach (var (idx, start, size, _) in partitions)
        {
            partitionMap[idx] = (
                (uint)(start / MediaUnit),
                (uint)(AlignUp(size, MediaUnit) / MediaUnit)
            );
        }

        uint totalSizeInUnits = (uint)(output.Length / MediaUnit);

        // NCSD 헤더 (0x200 bytes)
        byte[] ncsdHdr = new byte[0x200];
        ncsdHdr[0x100] = (byte)'N'; ncsdHdr[0x101] = (byte)'C';
        ncsdHdr[0x102] = (byte)'S'; ncsdHdr[0x103] = (byte)'D';

        BinaryPrimitives.WriteUInt32LittleEndian(ncsdHdr.AsSpan(0x104), totalSizeInUnits);

        var part0 = partitions.FirstOrDefault(p => p.index == 0);
        if (part0 != default)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(ncsdHdr.AsSpan(0x108), part0.ncchHeader.ProgramId);
            part0.ncchHeader.ExtendedHeaderHash.CopyTo(ncsdHdr, 0x160);
        }

        for (int i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(ncsdHdr.AsSpan(0x120 + i * 8), partitionMap[i].offset);
            BinaryPrimitives.WriteUInt32LittleEndian(ncsdHdr.AsSpan(0x120 + i * 8 + 4), partitionMap[i].size);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(ncsdHdr.AsSpan(0x180), 0x312);
        ncsdHdr[0x18C] = 0x01;
        ncsdHdr[0x18D] = 0x01;

        foreach (var (idx, _, _, hdr) in partitions)
            BinaryPrimitives.WriteUInt64LittleEndian(ncsdHdr.AsSpan(0x190 + idx * 8), hdr.PartitionId);

        // CardInfo 헤더 (0x3E00 bytes)
        byte[] cardInfo = new byte[0x3E00];
        BinaryPrimitives.WriteUInt32LittleEndian(cardInfo.AsSpan(0x00), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(cardInfo.AsSpan(0x100), totalSizeInUnits);

        // 맨 앞에 씀
        output.Position = 0;
        output.Write(ncsdHdr);
        output.Write(cardInfo);
    }

    private static long AlignUp(long v, long a) => (v + a - 1) & ~(a - 1);
}