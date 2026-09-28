using DolphinTool.Core.Models;
using DolphinTool.Core.Rvz;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;

namespace DolphinTool.Core.Services.Wia;

internal sealed class WiaSource : IRvzInputSource, IWiiPartitionSource
{
    private readonly record struct Region(long Start, long End, int RawIndex, int PartitionIndex, int DataIndex);
    private readonly record struct CachedChunkData(byte[] Data, int Length, List<HashException>[] ExceptionLists);

    private sealed class Context(RvzCompressionType compression, byte[] compressorData) : IDisposable
    {
        public RvzChunkDecoder Decoder { get; } = new RvzChunkDecoder(compression, compressorData);
        public WiiGroupEncryptor Encryptor { get; } = new();
        public List<HashException> Exceptions { get; } = [];
        public byte[] Decrypted { get; } = new byte[WiiLayout.GroupDataSize];
        public byte[] Encrypted { get; } = new byte[WiiLayout.GroupTotalSize];
        public byte[] Input = [];
        public CachedChunkData CachedChunk;
        public bool HasCachedChunk;
        public long CachedChunkIndex = -1;
        public int CachedPartitionIndex = -1;
        public long CachedHashGroupStart = -1;
        public int CachedDataPartitionIndex = -1;
        public int CachedDataEntryIndex = -1;
        public int CachedRegionIndex = -1;

        public void EnsureInput(int size)
        {
            if (Input.Length < size)
                Input = new byte[size];
        }

        public void Dispose()
        {
            Decoder.Dispose();
            Encryptor.Dispose();
        }
    }

    private const long ChunkCacheByteBudget = 256L * 1024 * 1024;

    private readonly SafeFileHandle _handle;
    private readonly RvzFile _file;
    private readonly Region[] _regions;
    private readonly long _headerLength;
    private readonly long _partitionChunkSize;
    private readonly int _partitionExceptionLists;
    private readonly ThreadLocal<Context> _contexts;
    private readonly ConcurrentDictionary<long, Lazy<CachedChunkData>> _chunkCache = new();
    private readonly ConcurrentQueue<long> _chunkCacheOrder = new();
    private long _chunkCacheBytes;

    private WiaSource(SafeFileHandle handle, RvzFile file)
    {
        _handle = handle;
        _file = file;
        _headerLength = Math.Min(file.DiscHeader.Length, file.IsoSize);
        _partitionChunkSize = (long)file.ChunkSize * WiiLayout.BlockDataSize / WiiLayout.BlockTotalSize;
        _partitionExceptionLists = (int)Math.Max(1, _partitionChunkSize / WiiLayout.GroupDataSize);
        _regions = BuildRegions();
        _contexts = new ThreadLocal<Context>(() => new Context(_file.Compression, _file.CompressorData), true);
    }

    public long Length => _file.IsoSize;

    public static bool IsWia(SafeFileHandle handle)
    {
        if (RandomAccess.GetLength(handle) < 4)
            return false;

        Span<byte> magic = stackalloc byte[4];
        RvzIo.ReadExactly(handle, magic, 0);
        return RvzMagic.IsWia(magic);
    }

    public static WiaSource Open(SafeFileHandle handle)
    {
        try
        {
            var file = RvzFile.Open(handle);
            using (RvzDecompressor.Create(file.Compression, file.CompressorData))
            {
            }
            return new WiaSource(handle, file);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private Region[] BuildRegions()
    {
        var regions = new List<Region>(_file.RawEntries.Length + _file.Partitions.Sum(static p => p.DataEntries.Length));

        for (int i = 0; i < _file.RawEntries.Length; i++)
        {
            var entry = _file.RawEntries[i];

            if (entry.DataSize != 0)
                regions.Add(new Region(entry.DataOffset, entry.DataOffset + entry.DataSize, i, -1, -1));
        }

        for (int p = 0; p < _file.Partitions.Length; p++)
        {
            var entries = _file.Partitions[p].DataEntries;

            for (int d = 0; d < entries.Length; d++)
            {
                if (entries[d].SectorCount == 0)
                    continue;

                long start = (long)entries[d].FirstSector * WiiLayout.BlockTotalSize;
                long end = start + (long)entries[d].SectorCount * WiiLayout.BlockTotalSize;
                regions.Add(new Region(start, end, -1, p, d));
            }
        }

        regions.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return [.. regions];
    }

    public void Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > Length - destination.Length)
            throw new EndOfStreamException("WIA 범위를 벗어난 읽기입니다.");

        if (destination.Length == 0)
            return;

        var context = _contexts.Value!;
        int written = 0;

        while (written < destination.Length)
        {
            long current = offset + written;

            if (current < _headerLength)
            {
                int length = (int)Math.Min(_headerLength - current, destination.Length - written);
                Copy(_file.DiscHeader.AsSpan((int)current, length), destination.Slice(written, length));
                written += length;
                continue;
            }

            Region region = FindRegion(context, current);

            int lengthRead = region.PartitionIndex < 0
                ? ReadRaw(context, region, current, destination.Slice(written))
                : ReadPartition(context, region, current, destination.Slice(written));

            if (lengthRead <= 0)
                throw new InvalidDataException("WIA 읽기 진행에 실패했습니다.");

            written += lengthRead;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static unsafe void Copy(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length == 0)
            return;

        fixed (byte* src = source)
        fixed (byte* dst = destination)
            Buffer.MemoryCopy(src, dst, destination.Length, source.Length);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private Region FindRegion(Context context, long offset)
    {
        int cached = context.CachedRegionIndex;

        if ((uint)cached < (uint)_regions.Length)
        {
            var region = _regions[cached];

            if (offset >= region.Start && offset < region.End)
                return region;

            if (offset >= region.End && cached + 1 < _regions.Length)
            {
                var next = _regions[cached + 1];

                if (offset >= next.Start && offset < next.End)
                {
                    context.CachedRegionIndex = cached + 1;
                    return next;
                }
            }
        }

        int lo = 0;
        int hi = _regions.Length - 1;
        int found = -1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);

            if (_regions[mid].Start <= offset)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (found < 0 || offset >= _regions[found].End)
            throw new InvalidDataException("WIA 데이터 영역 사이에 빈 구간이 있습니다.");

        context.CachedRegionIndex = found;
        return _regions[found];
    }

    private int ReadRaw(Context context, Region region, long offset, Span<byte> destination)
    {
        var entry = _file.RawEntries[region.RawIndex];
        int length = (int)Math.Min(region.End - offset, destination.Length);

        long readOffset = offset;
        long remaining = length;
        int position = 0;

        ReadFromGroups(context, ref readOffset, ref remaining, destination.Slice(0, length), ref position,
            _file.ChunkSize, WiiLayout.BlockTotalSize, entry.DataOffset, entry.DataSize,
            entry.GroupIndex, entry.GroupCount, 0, null);

        if (remaining != 0)
            throw new InvalidDataException("WIA 원본 데이터 그룹이 부족합니다.");

        return length;
    }

    private int ReadPartition(Context context, Region region, long offset, Span<byte> destination)
    {
        var partition = _file.Partitions[region.PartitionIndex];
        long partitionStart = (long)partition.FirstSector * WiiLayout.BlockTotalSize;
        long relative = offset - partitionStart;
        long hashGroupIndex = relative / WiiLayout.GroupTotalSize;
        long hashGroupStart = hashGroupIndex * WiiLayout.GroupTotalSize;
        long groupStartSector = hashGroupIndex * WiiLayout.BlocksPerGroup;
        int validSectors = (int)Math.Min(WiiLayout.BlocksPerGroup, partition.TotalSectors - groupStartSector);

        if (validSectors <= 0)
            throw new InvalidDataException("WIA 파티션 섹터 범위가 올바르지 않습니다.");

        if (context.CachedPartitionIndex != region.PartitionIndex || context.CachedHashGroupStart != hashGroupStart)
        {
            ReadDecryptedGroup(context, region.PartitionIndex, partition, groupStartSector, validSectors, region.DataIndex);
            context.Encryptor.Encrypt(partition.Key, context.Decrypted, context.Exceptions, context.Encrypted);
            context.CachedPartitionIndex = region.PartitionIndex;
            context.CachedHashGroupStart = hashGroupStart;
        }

        int offsetInGroup = (int)(offset - partitionStart - hashGroupStart);
        int length = (int)Math.Min(
            WiiLayout.GroupTotalSize - offsetInGroup,
            Math.Min(region.End - offset, destination.Length));

        Copy(context.Encrypted.AsSpan(offsetInGroup, length), destination.Slice(0, length));
        return length;
    }

    public bool TryReadDecryptedHashGroup(long readOffset, int blocksInThisGroup, byte[] decrypted, List<HashException> exceptions)
    {
        if (readOffset < _headerLength)
            return false;

        var context = _contexts.Value!;

        Region region;

        try
        {
            region = FindRegion(context, readOffset);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        if (region.PartitionIndex < 0)
            return false;

        var partition = _file.Partitions[region.PartitionIndex];
        long partitionStart = (long)partition.FirstSector * WiiLayout.BlockTotalSize;
        long relative = readOffset - partitionStart;

        if (relative < 0 || relative % WiiLayout.GroupTotalSize != 0)
            return false;

        long groupStartSector = relative / WiiLayout.BlockTotalSize;
        int validSectors = (int)Math.Min(WiiLayout.BlocksPerGroup, partition.TotalSectors - groupStartSector);

        if (validSectors != blocksInThisGroup || decrypted.Length < WiiLayout.GroupDataSize)
            return false;

        ReadDecryptedGroup(context, region.PartitionIndex, partition, groupStartSector, validSectors, region.DataIndex);
        Copy(context.Decrypted.AsSpan(0, WiiLayout.GroupDataSize), decrypted);

        exceptions.Clear();
        exceptions.AddRange(context.Exceptions);
        return true;
    }

    private void ReadDecryptedGroup(Context context, int partitionIndex, PartitionEntry partition, long groupStartSector, int validSectors, int entryHint)
    {
        context.Exceptions.Clear();

        long offset = groupStartSector * WiiLayout.BlockDataSize;
        long remaining = (long)validSectors * WiiLayout.BlockDataSize;
        int position = 0;

        int startEntry;

        if (context.CachedDataPartitionIndex == partitionIndex && context.CachedDataEntryIndex >= 0)
        {
            startEntry = context.CachedDataEntryIndex;

            if (startEntry >= partition.DataEntries.Length)
                startEntry = partition.DataEntries.Length - 1;
        }
        else
        {
            startEntry = FindPartitionDataEntry(partition, groupStartSector, entryHint);
        }

        if (startEntry < 0)
            throw new InvalidDataException("WIA 파티션 데이터 엔트리를 찾을 수 없습니다.");

        var entries = partition.DataEntries;

        for (int i = startEntry; i < entries.Length && remaining > 0; i++)
        {
            var entry = entries[i];

            if (entry.SectorCount == 0)
                continue;

            long dataOffset = ((long)entry.FirstSector - partition.FirstSector) * WiiLayout.BlockDataSize;
            long dataSize = (long)entry.SectorCount * WiiLayout.BlockDataSize;

            if (dataOffset + dataSize <= offset)
                continue;

            if (dataOffset > offset)
                throw new InvalidDataException("WIA 데이터 영역 사이에 빈 구간이 있습니다.");

            ReadFromGroups(context, ref offset, ref remaining, context.Decrypted, ref position,
                _partitionChunkSize, WiiLayout.BlockDataSize, dataOffset, dataSize,
                entry.GroupIndex, entry.GroupCount, _partitionExceptionLists, context.Exceptions);

            context.CachedDataPartitionIndex = partitionIndex;
            context.CachedDataEntryIndex = i;
        }

        if (remaining != 0)
            throw new InvalidDataException("WIA 파티션 데이터 그룹이 부족합니다.");

        if (position < context.Decrypted.Length)
            context.Decrypted.AsSpan(position).Clear();
    }

    private static int FindPartitionDataEntry(PartitionEntry partition, long groupStartSector, int hint)
    {
        var entries = partition.DataEntries;

        if ((uint)hint < (uint)entries.Length)
        {
            var entry = entries[hint];

            if (entry.SectorCount != 0)
            {
                long start = entry.FirstSector - partition.FirstSector;
                long end = start + entry.SectorCount;

                if (groupStartSector >= start && groupStartSector < end)
                    return hint;

                if (groupStartSector >= end)
                {
                    for (int i = hint + 1; i < entries.Length; i++)
                    {
                        entry = entries[i];

                        if (entry.SectorCount == 0)
                            continue;

                        start = entry.FirstSector - partition.FirstSector;
                        end = start + entry.SectorCount;

                        if (groupStartSector < start)
                            break;

                        if (groupStartSector < end)
                            return i;
                    }
                }
            }
        }

        long targetSector = partition.FirstSector + groupStartSector;
        int lo = 0;
        int hi = entries.Length - 1;
        int found = -1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            var entry = entries[mid];

            if (entry.SectorCount == 0 || entry.FirstSector <= targetSector)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        while (found >= 0 && entries[found].SectorCount == 0)
            found--;

        return found;
    }

    private void ReadFromGroups(
        Context context,
        ref long offset,
        ref long size,
        Span<byte> destination,
        ref int destinationPosition,
        long chunkSize,
        int sectorSize,
        long dataOffset,
        long dataSize,
        uint groupIndex,
        uint groupCount,
        int exceptionLists,
        List<HashException>? exceptions)
    {
        if (dataOffset + dataSize <= offset)
            return;

        if (offset < dataOffset)
            throw new InvalidDataException("WIA 데이터 영역 사이에 빈 구간이 있습니다.");

        long skipped = dataOffset % sectorSize;
        dataOffset -= skipped;
        dataSize += skipped;

        long startGroup = (offset - dataOffset) / chunkSize;

        for (long i = startGroup; i < groupCount && size > 0; i++)
        {
            long totalGroupIndex = groupIndex + i;

            if ((ulong)totalGroupIndex >= (ulong)_file.Groups.Length)
                throw new InvalidDataException("WIA 그룹 인덱스가 범위를 벗어났습니다.");

            var group = _file.Groups[totalGroupIndex];
            long groupOffsetInData = i * chunkSize;
            long offsetInGroup = offset - groupOffsetInData - dataOffset;
            long thisChunkSize = Math.Min(chunkSize, dataSize - groupOffsetInData);
            long bytesToRead = Math.Min(thisChunkSize - offsetInGroup, size);

            if (offsetInGroup < 0 || bytesToRead <= 0)
                throw new InvalidDataException("WIA 그룹 오프셋이 올바르지 않습니다.");

            int bytes = (int)bytesToRead;
            int destinationOffset = destinationPosition;

            if (group.DataSize == 0)
            {
                destination.Slice(destinationOffset, bytes).Clear();
            }
            else
            {
                var chunk = GetChunk(context, totalGroupIndex, group, (int)thisChunkSize, exceptionLists, groupOffsetInData);

                Copy(
                    chunk.Data.AsSpan((int)offsetInGroup, bytes),
                    destination.Slice(destinationOffset, bytes));

                if (exceptions != null && exceptionLists > 0)
                {
                    int listIndex = (int)(offsetInGroup / WiiLayout.GroupDataSize);
                    int additional = (int)(groupOffsetInData % WiiLayout.GroupDataSize / WiiLayout.BlockDataSize * WiiLayout.BlockHeaderSize);

                    if ((uint)listIndex >= (uint)chunk.ExceptionLists.Length)
                        throw new InvalidDataException("WIA 해시 예외 목록 인덱스가 올바르지 않습니다.");

                    foreach (var exception in chunk.ExceptionLists[listIndex])
                    {
                        int adjusted = exception.Offset + additional;

                        if ((uint)adjusted > ushort.MaxValue)
                            throw new InvalidDataException("WIA 해시 예외 오프셋이 올바르지 않습니다.");

                        exceptions.Add(new HashException((ushort)adjusted, exception.Hash));
                    }
                }
            }

            offset += bytesToRead;
            size -= bytesToRead;
            destinationPosition += bytes;
        }
    }

    private CachedChunkData GetChunk(Context context, long totalGroupIndex, GroupEntry group, int dataSize, int exceptionLists, long junkOffset)
    {
        if (context.HasCachedChunk && context.CachedChunkIndex == totalGroupIndex)
            return context.CachedChunk;

        CachedChunkData result;

        if (exceptionLists > 0 && dataSize <= WiiLayout.GroupDataSize)
        {
            var decoded = DecodeChunk(context, group, dataSize, exceptionLists, junkOffset);
            result = new CachedChunkData(decoded.Data, decoded.Length, decoded.ExceptionLists);
        }
        else
        {
            if (_chunkCache.TryGetValue(totalGroupIndex, out var lazy))
            {
                result = lazy.Value;
            }
            else
            {
                var newLazy = new Lazy<CachedChunkData>(
                    () => DecodeAndCache(context, totalGroupIndex, group, dataSize, exceptionLists, junkOffset),
                    LazyThreadSafetyMode.ExecutionAndPublication);

                lazy = _chunkCache.GetOrAdd(totalGroupIndex, newLazy);
                result = lazy.Value;
            }
        }

        context.CachedChunkIndex = totalGroupIndex;
        context.CachedChunk = result;
        context.HasCachedChunk = true;
        return result;
    }

    private DecodedChunk DecodeChunk(Context context, GroupEntry group, int dataSize, int exceptionLists, long junkOffset)
    {
        long fileOffset = group.FileOffset;
        int compressedSize = group.DataSize;

        if (fileOffset < 0 || compressedSize < 0 || fileOffset > _file.FileLength - compressedSize)
            throw new InvalidDataException("WIA 그룹 위치가 파일 범위를 벗어났습니다.");

        context.EnsureInput(compressedSize);
        RvzIo.ReadExactly(_handle, context.Input.AsSpan(0, compressedSize), fileOffset);

        return context.Decoder.Decode(
            context.Input.AsSpan(0, compressedSize),
            _file.Compression != RvzCompressionType.None,
            exceptionLists,
            dataSize,
            0,
            junkOffset);
    }

    private CachedChunkData DecodeAndCache(Context context, long totalGroupIndex, GroupEntry group, int dataSize, int exceptionLists, long junkOffset)
    {
        var decoded = DecodeChunk(context, group, dataSize, exceptionLists, junkOffset);

        var cachedData = GC.AllocateUninitializedArray<byte>(decoded.Length);
        Copy(decoded.Data.AsSpan(0, decoded.Length), cachedData);

        var cached = new CachedChunkData(cachedData, decoded.Length, decoded.ExceptionLists);

        _chunkCacheOrder.Enqueue(totalGroupIndex);
        Interlocked.Add(ref _chunkCacheBytes, cached.Length);
        TrimChunkCache();

        return cached;
    }

    private void TrimChunkCache()
    {
        while (Volatile.Read(ref _chunkCacheBytes) > ChunkCacheByteBudget && _chunkCacheOrder.TryDequeue(out var oldKey))
        {
            if (_chunkCache.TryRemove(oldKey, out var removed) && removed.IsValueCreated)
                Interlocked.Add(ref _chunkCacheBytes, -removed.Value.Length);
        }
    }

    public void Dispose()
    {
        foreach (var context in _contexts.Values)
            context.Dispose();

        _contexts.Dispose();
        _handle.Dispose();
    }
}