// WuDiscReader.cs
//
// C# port of maki-chan/wudecrypt (https://github.com/maki-chan/wudecrypt, AGPLv3).
// Parses the partition table and per-partition file system table (FST) of a Wii U
// disc image (read through WudReader, so both .wud and .wux work transparently),
// derives GM (game) partition keys from the title keys stored in the SI/GI
// partitions, and extracts individual files (handling both the "hashed" SHA-1
// hash-tree content format and the plain "unhashed" format).
//
// All disc-side crypto is AES-128-CBC with PaddingMode.None (data is always a
// multiple of the block size). Nothing here embeds Nintendo's actual common key —
// both the common key and the per-disc key must come from WiiUKeyProvider /
// an external "disc key" file supplied by the user.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WiiU.Core.Services;

public sealed class WuCluster
{
    public long Offset;   // byte offset, relative to partition start, already -0x8000 adjusted
    public long Size;
    public uint Unknown1;
    public uint Unknown2;
}

public sealed class WuFstEntry
{
    public bool IsDirectory;
    public uint NameOffset;
    public string EntryName = "";
    public long OffsetInCluster;
    public uint LastRowInDir; // only valid for directories: exclusive end index of children range
    public long Size;         // only valid for files
    public ushort Unknown;
    public ushort StartingCluster;
}

public sealed class WuPartition
{
    public string Name = "";        // full null-terminated partition name, e.g. "GM0005000E10102000"
    public string Identifier = "";  // first 0x19 bytes
    public long Offset;             // partition offset within the disc (already table-decoded)
    public byte[] Key = Array.Empty<byte>();
    public byte[] Iv = Array.Empty<byte>();
    public List<WuCluster> Clusters = new();
    public List<WuFstEntry> Entries = new();

    public string TypeCode => Name.Length >= 2 ? Name[..2] : "";
}

public sealed class WuFileEntry
{
    public string ParentPath = "";
    public string FileName = "";
    public WuPartition Partition = null!;
    public int EntryIndex;
}

public sealed class WuDiscReader
{
    private const long WiiUDecryptedAreaOffset = 0x18000;
    private const int PartitionTocOffset = 0x800;
    private const int PtocSize = 0x80;
    private static readonly byte[] DiscMagic = { 0x57, 0x55, 0x50, 0x2D }; // "WUP-"
    private static readonly byte[] DecryptedAreaSignature = { 0xCC, 0xA6, 0xE6, 0x7B };
    private static readonly byte[] FstSignature = { 0x46, 0x53, 0x54, 0x00 }; // "FST\0"

    private readonly WudReader _wud;
    private readonly byte[] _discKey;

    public List<WuPartition> Partitions { get; } = new();

    private WuDiscReader(WudReader wud, byte[] discKey)
    {
        _wud = wud;
        _discKey = discKey;
    }

    public static WuDiscReader Open(WudReader wud, byte[] discKey, WiiUKeyProvider keys)
    {
        var reader = new WuDiscReader(wud, discKey);
        reader.Initialize(keys);
        return reader;
    }

    private void Initialize(WiiUKeyProvider keys)
    {
        Span<byte> magic = stackalloc byte[4];
        _wud.ReadData(magic, 0);
        if (!magic.SequenceEqual(DiscMagic))
            throw new InvalidDataException("This does not look like a valid Wii U disc image (missing \"WUP-\" magic).");

        var toc = ReadEncryptedOffset(_discKey, WiiUDecryptedAreaOffset, 0x8000);
        if (!toc.AsSpan(0, 4).SequenceEqual(DecryptedAreaSignature))
            throw new InvalidDataException("Could not decrypt partition table — wrong disc key?");

        uint partitionCount = BE32(toc, 0x1C);

        // titleId (hex, uppercase, 16 chars) -> (titleKey, iv) derived from SI/GI tickets
        var titleKeys = new Dictionary<string, (byte[] Key, byte[] Iv)>(StringComparer.Ordinal);

        for (uint i = 0; i < partitionCount; i++)
        {
            int entryOffset = PartitionTocOffset + (int)(i * PtocSize);
            string identifier = ReadFixedAscii(toc, entryOffset, 0x19);
            string name = ReadNullTerminatedAscii(toc, entryOffset, PtocSize);
            uint rawOffset = BE32(toc, entryOffset + 0x20);
            long partitionOffset = (long)rawOffset * 0x8000 - 0x10000;

            var partition = new WuPartition { Identifier = identifier, Name = name, Offset = partitionOffset };
            string typeCode = partition.TypeCode;
            string hashName18 = name.Length >= 18 ? name[..18] : name;

            byte[]? key = null;
            byte[]? iv = null;
            if (typeCode is "SI" or "UP" or "GI")
            {
                key = _discKey;
                iv = new byte[16];
            }
            else if (titleKeys.TryGetValue(hashName18, out var tk))
            {
                key = tk.Key;
                iv = tk.Iv;
            }

            if (key is null)
            {
                // No known key for this partition (e.g. an unrecognized GM before its SI has
                // been scanned, or a genuinely unsupported partition type) — skip it.
                Partitions.Add(partition);
                continue;
            }

            partition.Key = key;
            partition.Iv = iv!;
            ParseFst(partition);
            Partitions.Add(partition);

            if (typeCode is "SI" or "GI")
                CollectTitleKeysFromTicket(partition, keys, titleKeys);
        }
    }

    private void CollectTitleKeysFromTicket(WuPartition partition, WiiUKeyProvider keys,
        Dictionary<string, (byte[] Key, byte[] Iv)> titleKeys)
    {
        foreach (var entry in partition.Entries)
        {
            if (entry.IsDirectory) continue;
            if (!string.Equals(entry.EntryName, "TITLE.TIK", StringComparison.OrdinalIgnoreCase)) continue;

            var cluster = partition.Clusters[entry.StartingCluster];
            byte[] encryptedTitleKey = ReadVolumeEncryptedOffset(partition, cluster, entry.OffsetInCluster + 0x1BF, 16);
            byte[] titleIdBytes = ReadVolumeEncryptedOffset(partition, cluster, entry.OffsetInCluster + 0x1DC, 8);

            var iv = new byte[16];
            titleIdBytes.CopyTo(iv, 0);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = keys.CommonKey.ToArray();
            aes.IV = iv;
            var titleKey = new byte[16];
            using var decryptor = aes.CreateDecryptor();
            decryptor.TransformBlock(encryptedTitleKey, 0, 16, titleKey, 0);

            string titleIdHexUpper = Convert.ToHexString(titleIdBytes); // uppercase hex, 16 chars
            titleKeys["GM" + titleIdHexUpper] = (titleKey, iv);
        }
    }

    // ---------------------------------------------------------------
    // FST parsing
    // ---------------------------------------------------------------

    private void ParseFst(WuPartition partition)
    {
        int ftSize = 0x8000;
        byte[] block = ReadVolumeEncryptedOffset(partition, WiiUDecryptedAreaOffset + partition.Offset, ftSize);
        if (!block.AsSpan(0, 4).SequenceEqual(FstSignature))
            throw new InvalidDataException($"Partition {partition.Name} did not decrypt to a valid FST (signature mismatch) — wrong key?");

        uint clusterCount = BE32(block, 8);
        uint clusterTableStride = BE32(block, 4); // observed to equal the 0x20-byte cluster descriptor size
        for (uint c = 0; c < clusterCount; c++)
        {
            int off = 0x20 + (int)(0x20 * c);
            long clusterStart = (long)BE32(block, off) * 0x8000;
            var cluster = new WuCluster
            {
                Offset = clusterStart > 0 ? clusterStart - 0x8000 : 0,
                Size = (long)BE32(block, off + 4) * 0x8000,
                Unknown1 = BE32(block, off + 0x10),
                Unknown2 = BE32(block, off + 0x14),
            };
            partition.Clusters.Add(cluster);
        }

        long entriesOffset = (long)clusterTableStride * clusterCount + 0x20;

        WuFstEntry ReadEntry(ReadOnlySpan<byte> raw)
        {
            var e = new WuFstEntry();
            if (raw[0] == 1)
            {
                e.IsDirectory = true;
                e.LastRowInDir = BinaryPrimitives.ReadUInt32BigEndian(raw.Slice(8, 4));
            }
            else
            {
                e.IsDirectory = false;
                e.Size = BinaryPrimitives.ReadUInt32BigEndian(raw.Slice(8, 4));
            }
            e.NameOffset = BinaryPrimitives.ReadUInt32BigEndian(raw[..4]) & 0x00FFFFFF;
            e.OffsetInCluster = (long)BinaryPrimitives.ReadUInt32BigEndian(raw.Slice(4, 4)) << 5;
            e.Unknown = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(0xC, 2));
            e.StartingCluster = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(0xE, 2));
            return e;
        }

        EnsureFstLoaded(partition, ref block, ref ftSize, entriesOffset + 16);
        var root = ReadEntry(block.AsSpan((int)entriesOffset, 16));
        uint totalEntries = root.LastRowInDir;
        long nameTableOffset = entriesOffset + totalEntries * 16;

        EnsureFstLoaded(partition, ref block, ref ftSize, nameTableOffset + root.NameOffset + 0x200);
        root.EntryName = ReadNullTerminatedAscii(block, (int)(nameTableOffset + root.NameOffset), 0x200);
        partition.Entries.Add(root);

        for (uint j = 1; j < totalEntries; j++)
        {
            long currentEntryOffset = entriesOffset + j * 16;
            EnsureFstLoaded(partition, ref block, ref ftSize, currentEntryOffset + 16);
            var entry = ReadEntry(block.AsSpan((int)currentEntryOffset, 16));

            long currentNameOffset = nameTableOffset + entry.NameOffset;
            EnsureFstLoaded(partition, ref block, ref ftSize, currentNameOffset + 0x200);
            entry.EntryName = ReadNullTerminatedAscii(block, (int)currentNameOffset, 0x200);

            partition.Entries.Add(entry);
        }
    }

    private void EnsureFstLoaded(WuPartition partition, ref byte[] block, ref int ftSize, long neededSize)
    {
        while (neededSize > ftSize)
        {
            ftSize += 0x8000;
            block = ReadVolumeEncryptedOffset(partition, WiiUDecryptedAreaOffset + partition.Offset, ftSize);
        }
    }

    // ---------------------------------------------------------------
    // Directory tree walking (mirrors create_directory/extract_dir)
    // ---------------------------------------------------------------

    /// <summary>Enumerates every file entry in a partition as (fullVirtualPath, entry).</summary>
    public IEnumerable<(string Path, WuFileEntry File)> EnumerateFiles(WuPartition partition)
    {
        if (partition.Entries.Count == 0) yield break;
        int index = 0;
        foreach (var item in WalkDirectory(partition, ref index, ""))
            yield return item;
    }

    private IEnumerable<(string, WuFileEntry)> WalkDirectory(WuPartition partition, ref int index, string parentPath)
    {
        var dirEntry = partition.Entries[index];
        string dirPath = parentPath.Length == 0 ? dirEntry.EntryName : $"{parentPath}/{dirEntry.EntryName}";
        uint lastRow = dirEntry.LastRowInDir;
        index++;

        var results = new List<(string, WuFileEntry)>();
        while (index < lastRow)
        {
            var entry = partition.Entries[index];
            if (entry.IsDirectory)
            {
                int localIndex = index;
                foreach (var sub in WalkDirectory(partition, ref localIndex, dirPath))
                    results.Add(sub);
                index = localIndex;
            }
            else
            {
                results.Add(($"{dirPath}/{entry.EntryName}", new WuFileEntry
                {
                    ParentPath = dirPath,
                    FileName = entry.EntryName,
                    Partition = partition,
                    EntryIndex = index
                }));
                index++;
            }
        }
        return results;
    }

    // ---------------------------------------------------------------
    // File extraction (mirrors extract_file / extract_file_hashed / extract_file_unhashed)
    // ---------------------------------------------------------------

    public void ExtractFileTo(WuFileEntry file, Stream destination)
    {
        var partition = file.Partition;
        var entry = partition.Entries[file.EntryIndex];
        var cluster = partition.Clusters[entry.StartingCluster];

        Span<byte> firstIv = stackalloc byte[16];
        // cluster id, byte-swapped into the first two IV bytes (matches original's
        // little-endian reinterpretation of the big-endian-stored StartingCluster)
        firstIv[0] = (byte)(entry.StartingCluster & 0xFF);
        firstIv[1] = (byte)(entry.StartingCluster >> 8);

        bool isHashed = entry.Unknown == 0x0400 || entry.Unknown == 0x0040
            || (cluster.Unknown1 == 0x00000400 && cluster.Unknown2 == 0x02000000);

        if (isHashed)
            ExtractHashed(partition, cluster, entry, firstIv, destination);
        else
            ExtractUnhashed(partition, cluster, entry, firstIv, destination);
    }

    private void ExtractUnhashed(WuPartition partition, WuCluster cluster, WuFstEntry entry, ReadOnlySpan<byte> iv, Stream destination)
    {
        long size = entry.Size;
        long fileOffset = entry.OffsetInCluster;
        var ivArr = iv.ToArray();

        while (size > 0)
        {
            long blockNumber = fileOffset / 0x8000;
            long blockOffset = fileOffset % 0x8000;
            long readOffset = WiiUDecryptedAreaOffset + partition.Offset + cluster.Offset + blockNumber * 0x8000;

            byte[] decrypted = ReadEncryptedOffsetWithIv(partition.Key, ivArr, readOffset, 0x8000);

            long maxCopy = 0x8000 - blockOffset;
            long copySize = Math.Min(size, maxCopy);
            destination.Write(decrypted, (int)blockOffset, (int)copySize);

            size -= copySize;
            fileOffset += copySize;
        }
    }

    private void ExtractHashed(WuPartition partition, WuCluster cluster, WuFstEntry entry, ReadOnlySpan<byte> iv, Stream destination)
    {
        const long blockSize = 0xFC00;
        long size = entry.Size;
        long fileOffset = entry.OffsetInCluster;
        var ivArr = iv.ToArray();
        var zeroIv = new byte[16];
        using var sha1 = SHA1.Create();

        while (size > 0)
        {
            long blockNumber = fileOffset / blockSize;
            long blockOffset = fileOffset - blockNumber * blockSize;
            long ivBlock = blockNumber & 0xF;

            long readOffset = WiiUDecryptedAreaOffset + partition.Offset + cluster.Offset + blockNumber * 0x10000;

            byte[] header = ReadEncryptedOffsetWithIv(partition.Key, ivArr, readOffset, 0x400);

            var clusterIv = new byte[16];
            var h0 = new byte[20];
            Array.Copy(header, (int)(ivBlock * 0x14), clusterIv, 0, 16);
            Array.Copy(header, (int)(ivBlock * 0x14), h0, 0, 20);
            if (ivBlock == 0)
                clusterIv[1] ^= (byte)entry.StartingCluster;

            byte[] decrypted = ReadEncryptedOffsetWithIv(partition.Key, clusterIv, readOffset + 0x400, (int)blockSize);

            var computedHash = sha1.ComputeHash(decrypted);
            if (ivBlock == 0)
                computedHash[1] ^= (byte)entry.StartingCluster;
            if (!computedHash.AsSpan().SequenceEqual(h0))
                throw new InvalidDataException($"SHA-1 hash mismatch while extracting \"{entry.EntryName}\" — corrupt dump or wrong key.");

            long maxCopy = blockSize - blockOffset;
            long copySize = Math.Min(size, maxCopy);
            destination.Write(decrypted, (int)blockOffset, (int)copySize);

            size -= copySize;
            fileOffset += copySize;
        }
    }

    // ---------------------------------------------------------------
    // Low-level encrypted reads
    // ---------------------------------------------------------------

    private byte[] ReadEncryptedOffset(byte[] key, long offset, int count)
        => ReadEncryptedOffsetWithIv(key, new byte[16], offset, count);

    private byte[] ReadEncryptedOffsetWithIv(byte[] key, byte[] iv, long offset, int count)
    {
        var cipher = new byte[count];
        int got = _wud.ReadData(cipher, offset);
        if (got != count)
            throw new EndOfStreamException($"Expected {count} bytes at offset 0x{offset:X}, got {got}.");

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv;
        var plain = new byte[count];
        using var decryptor = aes.CreateDecryptor();
        decryptor.TransformBlock(cipher, 0, count, plain, 0);
        return plain;
    }

    /// <summary>Reads and decrypts an arbitrary-length, arbitrary-offset span from a partition's
    /// volume area, re-deriving each 0x8000 physical block independently (matches wudecrypt's
    /// readVolumeEncryptedOffset: every block uses the SAME fixed IV, i.e. blocks are not chained).</summary>
    private byte[] ReadVolumeEncryptedOffset(WuPartition partition, WuCluster cluster, long fileOffsetInCluster, int size)
        => ReadVolumeEncryptedOffset(partition, WiiUDecryptedAreaOffset + partition.Offset + cluster.Offset, fileOffsetInCluster, size);

    private byte[] ReadVolumeEncryptedOffset(WuPartition partition, long baseOffset, long fileOffset, int size)
    {
        var output = new byte[size];
        int written = 0;
        long remaining = size;
        var zeroIv = new byte[16];

        while (remaining > 0)
        {
            long blockNumber = fileOffset / 0x8000;
            long blockOffset = fileOffset % 0x8000;
            long readOffset = baseOffset + blockNumber * 0x8000;

            byte[] decrypted = ReadEncryptedOffsetWithIv(partition.Key, zeroIv, readOffset, 0x8000);

            long maxCopy = 0x8000 - blockOffset;
            long copySize = Math.Min(remaining, maxCopy);
            Array.Copy(decrypted, blockOffset, output, written, copySize);

            remaining -= copySize;
            written += (int)copySize;
            fileOffset += copySize;
        }
        return output;
    }

    // convenience overload used for reading the partition table / FST itself (partition-less)
    private byte[] ReadVolumeEncryptedOffset(WuPartition partition, long baseOffset, int size)
        => ReadVolumeEncryptedOffset(partition, baseOffset, 0, size);

    // ---------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------

    private static uint BE32(byte[] buffer, int offset) => BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset, 4));

    private static string ReadFixedAscii(byte[] buffer, int offset, int length)
        => Encoding.ASCII.GetString(buffer, offset, length);

    private static string ReadNullTerminatedAscii(byte[] buffer, int offset, int maxLength)
    {
        int len = 0;
        while (len < maxLength && offset + len < buffer.Length && buffer[offset + len] != 0) len++;
        return Encoding.ASCII.GetString(buffer, offset, len);
    }
}