using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WiiU.Core.Services;

/// <summary>
/// title.tmd / title.tik / title.cert + 번호 매겨진 .app(+.h3) 콘텐츠 파일들로 구성된
/// "순수 WUP" 설치 폴더(로드라인 형태의 code/content/meta 서브폴더가 아닌, 평문 콘텐츠 파일들이
/// 그대로 있는 폴더)를 읽어들이는 ITitleSource 구현.
///
/// 구조: content index 0은 항상 FST(File System Table) 그 자체이며, WUD의 GM 파티션과 동일한
/// FST 포맷을 사용한다. FST의 클러스터 인덱스는 TMD 콘텐츠 인덱스와 1:1로 대응한다.
/// </summary>
public sealed class WupTitleSource : ITitleSource
{
    private const int RawFullDecryptSizeLimit = 64 * 1024 * 1024; // raw 콘텐츠는 이 크기 이하만 전체 복호화 캐싱

    private const int HashedBlockSize = 0x10000;
    private const int HashedHeaderSize = 0x400;
    private const int HashedDataSize = 0xFC00; // HashedBlockSize - HashedHeaderSize

    private readonly string _folder;
    private readonly byte[] _titleKey;
    private readonly List<WupContent> _contents;
    private readonly List<FstEntry> _entries = [];
    private readonly int _fstOffsetFactor;

    private readonly Dictionary<int, byte[]> _rawContentCache = [];
    private readonly Dictionary<int, FileStream> _hashedStreams = [];

    public string TitleIdHex { get; }

    public int TitleVersion { get; }

    /// <summary>이 폴더가 순수 WUP(설치 패키지) 폴더인지 판별한다. code/content/meta 서브폴더가 있는
    /// 로드라인 형태와는 다르게, title.tmd/title.tik가 폴더 최상위에 바로 있어야 한다.</summary>
    public static bool LooksLikeWupFolder(string folderPath) =>
        File.Exists(Path.Combine(folderPath, "title.tmd")) && File.Exists(Path.Combine(folderPath, "title.tik"));

    public WupTitleSource(string folderPath)
    {
        _folder = folderPath;

        string tmdPath = Path.Combine(folderPath, "title.tmd");
        string tikPath = Path.Combine(folderPath, "title.tik");

        if (!File.Exists(tmdPath)) throw new FileNotFoundException("title.tmd를 찾을 수 없습니다.", tmdPath);
        if (!File.Exists(tikPath)) throw new FileNotFoundException("title.tik를 찾을 수 없습니다.", tikPath);

        var tmdBytes = File.ReadAllBytes(tmdPath);
        var (titleIdHex, titleVersion, contents) = WupTmd.Parse(tmdBytes);

        TitleIdHex = titleIdHex;
        TitleVersion = titleVersion;
        _contents = contents;

        var tikBytes = File.ReadAllBytes(tikPath);
        var ticket = TitleTicket.Parse(tikBytes);

        _titleKey = ticket.DecryptTitleKey();

        // content index 0 = FST. 항상 raw(비해시) 콘텐츠이므로 전체 복호화해서 파싱한다.
        var fstContent = _contents.FirstOrDefault(c => c.Index == 0)
            ?? throw new InvalidDataException("TMD에서 index 0(FST) 콘텐츠를 찾을 수 없습니다.");

        byte[] fstData = DecryptRawContentFull(fstContent);

        _fstOffsetFactor = ParseFst(fstData);
    }

    #region FST Parsing

    private readonly record struct FstClusterInfo(bool IsHashed);

    private List<FstClusterInfo> _clusters = [];

    private sealed class FstEntry
    {
        public bool IsDirectory;
        public string Name = "";
        public int ParentDirIndex;
        public int DirEndIndex;
        public uint FileOffsetField;
        public uint FileSize;
        public ushort ClusterIndex;

        /// <summary>업데이트 패키지 전용 플래그. true면 "본편과 동일해서 이 콘텐츠엔 실제 데이터가 없고,
        /// 설치 시 본편 쪽 파일을 그대로 쓰는" 파일이다. 실제로 읽으려 하면 안 된다(클러스터가 텅 빈
        /// 자리표시자라서 곧바로 깨진다).</summary>
        public bool IsSharedWithBase;
    }

    private int ParseFst(byte[] fst)
    {
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(fst.AsSpan(0, 4));

        if (magic != 0x46535400) // "FST\0"
            throw new InvalidDataException("content 0이 FST가 아닙니다 (매직 불일치) — WUP 폴더 구조가 예상과 다릅니다.");

        uint offsetFactor = BinaryPrimitives.ReadUInt32BigEndian(fst.AsSpan(4, 4));
        uint numCluster = BinaryPrimitives.ReadUInt32BigEndian(fst.AsSpan(8, 4));

        if (numCluster > 4096)
            throw new InvalidDataException("FST 클러스터 개수가 비정상적으로 많습니다.");

        int clusterTableOffset = 0x20;

        for (int i = 0; i < numCluster; i++)
        {
            int off = clusterTableOffset + i * 0x20;
            byte hashMode = fst[off + 0x14];

            _clusters.Add(new FstClusterInfo(IsHashed: hashMode == 2));
        }

        int fileTableOffset = clusterTableOffset + (int)numCluster * 0x20;
        uint numFileEntries = BinaryPrimitives.ReadUInt32BigEndian(fst.AsSpan(fileTableOffset + 8, 4));

        int nameTableOffset = fileTableOffset + (int)numFileEntries * 0x10;

        for (int i = 0; i < numFileEntries; i++)
        {
            int eoff = fileTableOffset + i * 0x10;
            uint typeAndNameOffset = BinaryPrimitives.ReadUInt32BigEndian(fst.AsSpan(eoff, 4));
            uint offsetField = BinaryPrimitives.ReadUInt32BigEndian(fst.AsSpan(eoff + 4, 4));
            uint sizeField = BinaryPrimitives.ReadUInt32BigEndian(fst.AsSpan(eoff + 8, 4));
            ushort clusterIndex = BinaryPrimitives.ReadUInt16BigEndian(fst.AsSpan(eoff + 0xE, 2));

            bool isDir = ((typeAndNameOffset >> 24) & 0x01) != 0;

            // 최상위 비트(0x80000000)는 업데이트 패키지에서만 등장하는 플래그: "본편과 동일한 파일이라
            // 이 콘텐츠엔 실제 데이터가 없음"을 의미한다. 본편 FST에서는 이 비트가 켜진 파일을 실측으로
            // 확인한 적이 없어서(항상 0), 업데이트에서만 실측 확인된 케이스다.
            bool isSharedWithBase = (typeAndNameOffset & 0x80000000) != 0;

            uint nameOffset = typeAndNameOffset & 0xFFFFFF;
            string name = i == 0 ? "" : ReadCString(fst, nameTableOffset + (int)nameOffset);

            var entry = new FstEntry { IsDirectory = isDir, Name = name, ClusterIndex = clusterIndex, IsSharedWithBase = isSharedWithBase };

            if (isDir)
            {
                entry.ParentDirIndex = (int)offsetField;
                entry.DirEndIndex = (int)sizeField;
            }
            else
            {
                entry.FileOffsetField = offsetField;
                entry.FileSize = sizeField;
            }

            _entries.Add(entry);
        }

        return (int)offsetFactor;
    }

    private static string ReadCString(byte[] data, int offset)
    {
        if (offset < 0 || offset >= data.Length) return "";

        int end = offset;

        while (end < data.Length && data[end] != 0) end++;

        return Encoding.UTF8.GetString(data, offset, end - offset);
    }

    #endregion

    #region ITitleSource

    public IEnumerable<string> EnumerateFiles()
    {
        if (_entries.Count == 0) yield break;

        var pathStack = new Stack<(int EndIndex, string Path)>();

        pathStack.Push((_entries[0].DirEndIndex, string.Empty));

        int i = 1;

        while (i < _entries.Count)
        {
            while (pathStack.Count > 0 && i >= pathStack.Peek().EndIndex)
                pathStack.Pop();

            string parentPath = pathStack.Count > 0 ? pathStack.Peek().Path : string.Empty;
            var entry = _entries[i];
            string fullPath = parentPath.Length == 0 ? entry.Name : $"{parentPath}/{entry.Name}";

            if (entry.IsDirectory)
            {
                pathStack.Push((entry.DirEndIndex, fullPath));
                i++;
            }
            else
            {
                // 본편과 동일해서 이 콘텐츠엔 실제 데이터가 없는 파일은 애초에 목록에도 내보내지 않는다.
                // (업데이트 단독으로는 제공할 수 없는 파일이고, 클러스터도 텅 빈 자리표시자라 읽으면 바로 깨진다)
                if (!entry.IsSharedWithBase)
                    yield return fullPath;

                i++;
            }
        }
    }

    public long GetFileSize(string path)
    {
        var entry = FindEntry(path);

        if (entry is not null && entry.IsSharedWithBase)
            throw new InvalidOperationException($"'{path}'는 본편과 동일해서 이 업데이트 안에는 실제 데이터가 없습니다. 본편 쪽에서 읽어야 합니다.");

        return entry?.FileSize ?? 0;
    }

    public Stream OpenRead(string path)
    {
        var entry = FindEntry(path) ?? throw new FileNotFoundException($"WUP 안에서 파일을 찾을 수 없습니다: {path}");

        if (entry.IsSharedWithBase)
            throw new InvalidOperationException($"'{path}'는 본편과 동일해서 이 업데이트 안에는 실제 데이터가 없습니다. 본편 쪽에서 읽어야 합니다.");

        var buffer = new byte[entry.FileSize];

        try
        {
            ReadFileEntry(entry, 0, buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"'{path}' 읽기 실패 (clusterIndex={entry.ClusterIndex}, offsetField={entry.FileOffsetField}, " +
                $"byteOffset={(long)entry.FileOffsetField * _fstOffsetFactor}, size={entry.FileSize}): {ex.Message}", ex);
        }

        return new MemoryStream(buffer, writable: false);
    }

    private FstEntry? FindEntry(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int currentIndex = 0;
        int searchStart = 1;
        int searchEnd = _entries.Count > 0 ? _entries[0].DirEndIndex : 0;

        foreach (var part in parts)
        {
            int? found = null;
            int idx = searchStart;

            while (idx < searchEnd)
            {
                if (string.Equals(_entries[idx].Name, part, StringComparison.OrdinalIgnoreCase))
                {
                    found = idx;
                    break;
                }

                idx = _entries[idx].IsDirectory ? _entries[idx].DirEndIndex : idx + 1;
            }

            if (found is null) return null;

            currentIndex = found.Value;

            if (!_entries[currentIndex].IsDirectory)
            {
                searchStart = searchEnd = currentIndex;
                continue;
            }

            searchStart = currentIndex + 1;
            searchEnd = _entries[currentIndex].DirEndIndex;
        }

        return _entries[currentIndex];
    }

    #endregion

    #region Content Reading

    private void ReadFileEntry(FstEntry entry, long readOffset, byte[] dest, int destOffset, int size)
    {
        int clusterIndex = entry.ClusterIndex;
        long baseOffset = (long)entry.FileOffsetField * _fstOffsetFactor + readOffset;

        bool hashed = clusterIndex < _clusters.Count && _clusters[clusterIndex].IsHashed;

        if (hashed)
            ReadHashedRange(clusterIndex, baseOffset, dest, destOffset, size);
        else
            ReadRawRange(clusterIndex, baseOffset, dest, destOffset, size);
    }

    private WupContent FindContent(int index) =>
        _contents.FirstOrDefault(c => c.Index == index)
            ?? throw new InvalidDataException($"TMD에 콘텐츠 인덱스 {index}가 없습니다.");

    private void ReadRawRange(int clusterIndex, long offset, byte[] dest, int destOffset, int size)
    {
        if (!_rawContentCache.TryGetValue(clusterIndex, out var decrypted))
        {
            var content = FindContent(clusterIndex);

            decrypted = DecryptRawContentFull(content);
            _rawContentCache[clusterIndex] = decrypted;
        }

        if (offset + size > decrypted.Length)
            throw new InvalidDataException($"콘텐츠 {clusterIndex}에서 범위를 벗어난 읽기 요청입니다.");

        Array.Copy(decrypted, offset, dest, destOffset, size);
    }

    private byte[] DecryptRawContentFull(WupContent content)
    {
        string appPath = Path.Combine(_folder, $"{content.CIDHex}.app");

        if (!File.Exists(appPath))
            throw new FileNotFoundException($"콘텐츠 파일을 찾을 수 없습니다: {appPath}", appPath);

        var fi = new FileInfo(appPath);

        if (fi.Length > RawFullDecryptSizeLimit)
            throw new InvalidDataException($"콘텐츠 {content.CIDHex}.app가 raw 타입인데 비정상적으로 큽니다 ({fi.Length} bytes) — 해시트리 타입 판별이 잘못됐을 수 있습니다.");

        byte[] cipherData = File.ReadAllBytes(appPath);

        byte[] iv = new byte[16];

        iv[0] = (byte)(content.Index >> 8);
        iv[1] = (byte)(content.Index & 0xFF);

        AesCbcDecryptInPlace(cipherData, cipherData.Length, _titleKey, iv);

        return cipherData;
    }

    private void ReadHashedRange(int clusterIndex, long offset, byte[] dest, int destOffset, int size)
    {
        if (!_hashedStreams.TryGetValue(clusterIndex, out var stream))
        {
            var content = FindContent(clusterIndex);
            string appPath = Path.Combine(_folder, $"{content.CIDHex}.app");

            if (!File.Exists(appPath))
                throw new FileNotFoundException($"콘텐츠 파일을 찾을 수 없습니다: {appPath}", appPath);

            stream = File.OpenRead(appPath);
            _hashedStreams[clusterIndex] = stream;
        }

        int totalRead = 0;

        while (totalRead < size)
        {
            long blockIndex = offset / HashedDataSize;
            long offsetWithinBlock = offset % HashedDataSize;

            byte[] blockData = GetDecryptedHashedDataBlock(stream, blockIndex);

            int copyLen = (int)Math.Min(size - totalRead, HashedDataSize - offsetWithinBlock);

            Array.Copy(blockData, offsetWithinBlock, dest, destOffset + totalRead, copyLen);

            totalRead += copyLen;
            offset += copyLen;
        }
    }

    private byte[] GetDecryptedHashedDataBlock(FileStream stream, long blockIndex)
    {
        long absolute = blockIndex * HashedBlockSize;
        var block = new byte[HashedBlockSize];

        stream.Position = absolute;

        int got = ReadFully(stream, block, HashedBlockSize);

        // 마지막 블록은 데이터 부분(0xFC00)이 실제 콘텐츠 크기만큼만 채워져 있고 패딩 없이 파일이 끝나는
        // 경우가 흔하다 — 헤더(0x400)만 온전하면 그 뒤 데이터는 실제로 존재하는 만큼만 있어도 정상이다.
        if (got < HashedHeaderSize)
            throw new InvalidDataException("해시트리 콘텐츠 블록의 헤더(0x400바이트)조차 읽지 못했습니다 — 파일이 잘렸거나 오프셋 계산이 잘못됐을 수 있습니다.");

        // 블록 앞 0x400바이트(H0/H1/H2 해시 헤더)를 IV=0으로 복호화 — 이 헤더는 이 블록 전용이며 매 블록마다 새로 임베드되어 있음
        var hashPart = block.AsSpan(0, HashedHeaderSize).ToArray();

        AesCbcDecryptInPlace(hashPart, HashedHeaderSize, _titleKey, new byte[16]);

        // H0 테이블(16개 엔트리, 각 0x14바이트)에서 이 블록 자신의 해시를 찾아 그 앞 16바이트를 IV로 사용
        int h0Index = (int)(blockIndex % 16);
        var h0 = hashPart.AsSpan(h0Index * 20, 20).ToArray();
        var iv = h0.AsSpan(0, 16).ToArray();

        var dataPart = block.AsSpan(HashedHeaderSize, HashedDataSize).ToArray();

        // 이번에 실제로 읽어들인 데이터 바이트 수. 마지막 블록은 파일이 여기서 끝나서 0xFC00보다 적을 수 있다.
        // AES-CBC는 16바이트 배수만 복호화 가능하므로 그 이하로 내림한다(파일이 정상이라면 어차피 16의 배수여야 함).
        int dataAvailable = Math.Max(0, got - HashedHeaderSize);
        int alignedLen = dataAvailable - (dataAvailable % 16);

        if (alignedLen > 0)
            AesCbcDecryptInPlace(dataPart, alignedLen, _titleKey, iv);

        return dataPart;
    }

    /// <summary>스트림에서 count바이트를 다 채울 때까지 반복해서 읽는다. Stream.Read는 파일 중간이라도
    /// 요청한 만큼 다 안 읽고 부분적으로 반환할 수 있으므로, 한 번만 읽고 판단하면 안 된다.</summary>
    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        int total = 0;

        while (total < count)
        {
            int got = stream.Read(buffer, total, count - total);

            if (got == 0)
                break;

            total += got;
        }

        return total;
    }

    private static void AesCbcDecryptInPlace(byte[] data, int length, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();

        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();

        decryptor.TransformBlock(data, 0, length, data, 0);
    }

    #endregion

    public void Dispose()
    {
        foreach (var s in _hashedStreams.Values)
            s.Dispose();

        _hashedStreams.Clear();
        _rawContentCache.Clear();
    }
}

/// <summary>title.tmd에서 뽑아낸 콘텐츠 하나의 정보.</summary>
public sealed record WupContent(uint ContentId, ushort Index, ushort Type, ulong Size, byte[] Hash)
{
    public bool IsHashed => (Type & 0x0002) != 0;

    public string CIDHex => ContentId.ToString("x8");
}

/// <summary>title.tmd 파싱 전용 헬퍼. 실제 title.tmd 바이트를 오프셋 단위로 실측 검증해서 만든 파서.</summary>
internal static class WupTmd
{
    public static (string TitleIdHex, int TitleVersion, List<WupContent> Contents) Parse(byte[] tmd)
    {
        uint sigType = BinaryPrimitives.ReadUInt32BigEndian(tmd.AsSpan(0, 4));

        // Nintendo 서명 타입 표준 상수 (Wii/3DS/WiiU 공통): 서명 블록 크기는 타입에 의해 고정된다.
        int bodyStart = sigType switch
        {
            0x00010000 or 0x00010003 => 0x240, // RSA_4096 (SHA1 / SHA256)
            0x00010001 or 0x00010004 => 0x140, // RSA_2048 (SHA1 / SHA256) — 실측 검증된 케이스
            0x00010002 or 0x00010005 => 0x080, // ECDSA (SHA1 / SHA256)
            _ => throw new InvalidDataException($"지원하지 않는 TMD 서명 타입: 0x{sigType:X8}"),
        };

        ulong titleId = BinaryPrimitives.ReadUInt64BigEndian(tmd.AsSpan(bodyStart + 0x4C, 8));
        ushort titleVersion = BinaryPrimitives.ReadUInt16BigEndian(tmd.AsSpan(bodyStart + 0x9C, 2));
        ushort numContents = BinaryPrimitives.ReadUInt16BigEndian(tmd.AsSpan(bodyStart + 0x9E, 2));

        int contentInfoTableOffset = bodyStart + 0xC4;
        int contentTableOffset = contentInfoTableOffset + 64 * 36;

        var contents = new List<WupContent>(numContents);

        for (int i = 0; i < numContents; i++)
        {
            int off = contentTableOffset + i * 48;

            uint cid = BinaryPrimitives.ReadUInt32BigEndian(tmd.AsSpan(off, 4));
            ushort index = BinaryPrimitives.ReadUInt16BigEndian(tmd.AsSpan(off + 4, 2));
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(tmd.AsSpan(off + 6, 2));
            ulong size = BinaryPrimitives.ReadUInt64BigEndian(tmd.AsSpan(off + 8, 8));
            byte[] hash = tmd.AsSpan(off + 16, 32).ToArray();

            contents.Add(new WupContent(cid, index, type, size, hash));
        }

        return (titleId.ToString("x16"), titleVersion, contents);
    }
}