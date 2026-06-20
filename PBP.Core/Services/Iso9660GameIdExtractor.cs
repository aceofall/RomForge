using System.Text;
using System.Text.RegularExpressions;

namespace PBP.Core.Services;

public static class Iso9660GameIdExtractor
{
    public static string? Extract(Func<uint, byte[]> sectorReader)
    {
        var pvd = sectorReader(16);

        if (pvd[0] != 0x01 || pvd[1] != 'C' || pvd[2] != 'D' || pvd[3] != '0' || pvd[4] != '0' || pvd[5] != '1')
            return null;

        var rootDirLba = BitConverter.ToUInt32(pvd, 156 + 2);

        return FindSystemCnf(sectorReader, rootDirLba);
    }

    private static string? FindSystemCnf(Func<uint, byte[]> sectorReader, uint dirLba)
    {
        var sector = sectorReader(dirLba);
        var pos = 0;

        while (pos < sector.Length)
        {
            var recordLen = sector[pos];

            if (recordLen == 0)
                break;

            var nameLen = sector[pos + 32];
            var name = Encoding.ASCII.GetString(sector, pos + 33, nameLen).Split(';')[0].ToUpperInvariant();

            if (name == "SYSTEM.CNF")
            {
                var fileLba = BitConverter.ToUInt32(sector, pos + 2);

                return ReadSystemCnf(sectorReader, fileLba);
            }

            pos += recordLen;
        }

        return null;
    }

    private static string? ReadSystemCnf(Func<uint, byte[]> sectorReader, uint lba)
    {
        var content = Encoding.ASCII.GetString(sectorReader(lba));
        var boot = Regex.Match(content, @"BOOT\s*=\s*cdrom[:\\\/]+([A-Z]{4}_\d{3}\.\d+)", RegexOptions.IgnoreCase);

        if (!boot.Success) 
            return null;

        var id = Regex.Match(boot.Groups[1].Value.ToUpperInvariant(), @"([A-Z]{4})_(\d{3})\.(\d+)");

        if (!id.Success) 
            return null;

        return $"{id.Groups[1].Value}{id.Groups[2].Value}{id.Groups[3].Value}";
    }
}