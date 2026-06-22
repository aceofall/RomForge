using CHD.Core.Interop;
using CHD.Core.Models;

namespace PBP.Core.Services;

public class ChdReadStream(LibChdrWrapper wrapper, long totalLength, ChdInfo info) : Stream
{
    private long _position;
    private byte[]? _currentHunk;
    private uint _cachedHunkIndex = uint.MaxValue;
    private readonly uint _sectorsPerHunk = (wrapper.Header?.hunkbytes ?? 0) / 2448u;
    private readonly TrackRegion[] _tracks = BuildTrackRegions(info);

    private record TrackRegion(long Start, long PreGapEnd, long End);

    private static TrackRegion[] BuildTrackRegions(ChdInfo info)
    {
        var regions = new TrackRegion[info.Tracks.Length];
        long current = 0;

        for (int i = 0; i < info.Tracks.Length; i++)
        {
            var track = info.Tracks[i];
            long start = current;
            long pregapEnd = start + (long)track.PreGap * 2352;
            long end = pregapEnd + (long)track.Frames * 2352;

            regions[i] = new TrackRegion(start, pregapEnd, end);
            current = end;
        }

        return regions;
    }

    // 현재 position이 프리갭 구간인지, 아니면 데이터 구간인지 판별
    // 데이터 구간이면 CHD에서의 실제 섹터 인덱스 반환
    private (bool isPreGap, long chdSectorIdx) GetSectorInfo(long position)
    {
        long chdSectors = 0;

        for (int i = 0; i < _tracks.Length; i++)
        {
            var t = _tracks[i];

            if (position < t.PreGapEnd)
                return (true, 0);

            if (position < t.End)
                return (false, chdSectors + (position - t.PreGapEnd) / 2352);

            chdSectors += (t.End - t.PreGapEnd) / 2352;
        }

        return (true, 0);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = 0;

        while (bytesRead < count && _position < totalLength)
        {
            var (isPreGap, chdSectorIdx) = GetSectorInfo(_position);
            int posInSector = (int)(_position % 2352);
            int toRead = (int)Math.Min(count - bytesRead, 2352 - posInSector);
            toRead = (int)Math.Min(toRead, totalLength - _position);

            if (isPreGap)
            {
                // 프리갭 구간은 0으로 채움
                Array.Clear(buffer, offset + bytesRead, toRead);
            }
            else
            {
                uint hunkIdx = (uint)(chdSectorIdx / _sectorsPerHunk);
                int hunkOffset = (int)(chdSectorIdx % _sectorsPerHunk) * 2448;

                if (_cachedHunkIndex != hunkIdx)
                {
                    _currentHunk = wrapper.ReadHunk(hunkIdx);
                    _cachedHunkIndex = hunkIdx;
                }

                if (_currentHunk == null)
                    throw new NullReferenceException(nameof(_currentHunk));

                Array.Copy(_currentHunk, hunkOffset + posInSector, buffer, offset + bytesRead, toRead);
            }

            bytesRead += toRead;
            _position += toRead;
        }

        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => totalLength + offset,
            _ => _position
        };
        _position = Math.Clamp(_position, 0, totalLength);
        return _position;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => totalLength;
    public override long Position { get => _position; set => _position = value; }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}