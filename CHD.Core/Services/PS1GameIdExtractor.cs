using CHD.Core.Interop;
using System.Text;
using System.Text.RegularExpressions;

namespace CHD.Core.Services;

public class PS1GameIdExtractor(LibChdrWrapper chd)
{
    private const uint SectorDataSize = 2448;
    private const uint RawSectorSize = 2352;
    private const uint CDROM_TRACK_METADATA2_TAG = 0x54524B32;
    private const uint CDROM_TRACK_METADATA_TAG = 0x54524B20;
    private readonly uint _sectorsPerHunk = chd.Header!.Value.hunkbytes / SectorDataSize;

    private byte[] ReadSector(uint fileOffset, uint lba)
    {
        uint frame = fileOffset + lba;
        uint hunkIndex = frame / _sectorsPerHunk;
        uint hunkOffset = (frame % _sectorsPerHunk) * SectorDataSize;
        byte[] hunk = chd.ReadHunk(hunkIndex);
        byte[] sector = new byte[RawSectorSize];
        Buffer.BlockCopy(hunk, (int)hunkOffset, sector, 0, (int)RawSectorSize);

        return sector;
    }

    private uint GetTrack1FileOffset()
    {
        int pregapFrames = 0;
        bool pregapInFile = false;

        string? meta = chd.GetMetadata(CDROM_TRACK_METADATA2_TAG, 0);

        if (meta != null)
        {
            var m = Regex.Match(meta, @"TRACK:(\d+) TYPE:(\S+) SUBTYPE:(\S+) FRAMES:(\d+) PREGAP:(\d+) PGTYPE:(\S+) PGSUB:(\S+) POSTGAP:(\d+)");

            if (m.Success)
            {
                pregapFrames = int.Parse(m.Groups[5].Value);
                pregapInFile = pregapFrames > 0 && m.Groups[6].Value.StartsWith('V');
            }
        }

        return pregapInFile ? (uint)pregapFrames : 0u;
    }

    public string? ExtractGameId()
    {
        uint fileOffset = GetTrack1FileOffset();
        const int dataOffset = 24;
        byte[] pvd = ReadSector(fileOffset, 16);

        if (pvd[dataOffset] != 0x01 || pvd[dataOffset + 1] != 'C' || pvd[dataOffset + 2] != 'D' || pvd[dataOffset + 3] != '0' || pvd[dataOffset + 4] != '0' || pvd[dataOffset + 5] != '1')
            return null;

        uint rootDirLba = BitConverter.ToUInt32(pvd, dataOffset + 156 + 2);

        return FindSystemCnf(rootDirLba, fileOffset, dataOffset);
    }

    private string? FindSystemCnf(uint dirLba, uint fileOffset, int dataOffset)
    {
        byte[] sector = ReadSector(fileOffset, dirLba);
        int pos = dataOffset;
        int end = dataOffset + 2048;

        while (pos < end)
        {
            byte recordLen = sector[pos];
            if (recordLen == 0) break;

            byte nameLen = sector[pos + 32];
            string name = Encoding.ASCII.GetString(sector, pos + 33, nameLen).Split(';')[0].ToUpperInvariant();

            if (name == "SYSTEM.CNF")
            {
                uint fileLba = BitConverter.ToUInt32(sector, pos + 2);

                return ReadSystemCnf(fileLba, fileOffset, dataOffset);
            }

            pos += recordLen;
        }
        return null;
    }

    private string? ReadSystemCnf(uint lba, uint fileOffset, int dataOffset)
    {
        byte[] sector = ReadSector(fileOffset, lba);
        string content = Encoding.ASCII.GetString(sector, dataOffset, 2048);

        var boot = Regex.Match(content, @"BOOT\s*=\s*cdrom[:\\\/]+([A-Z]{4}_\d{3}\.\d+)", RegexOptions.IgnoreCase);

        if (!boot.Success) 
            return null;

        var id = Regex.Match(boot.Groups[1].Value.ToUpperInvariant(),
                             @"([A-Z]{4})_(\d{3})\.(\d+)");
        if (!id.Success) 
            return null;

        return $"{id.Groups[1].Value}{id.Groups[2].Value}{id.Groups[3].Value}";
    }
}