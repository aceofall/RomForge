namespace Patch.Core.Formats.DCP.Services;


public static class GdRomWriter
{
    public static void WriteDataTrack(string outputPath, uint trackStartLba, IEnumerable<(uint Lba, byte[] Sector2048)> sectors)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

        foreach (var (lba, userData) in sectors)
        {
            var raw = BuildRawSector(lba, userData);
            long byteOffset = (long)(lba - trackStartLba) * 2352;

            fs.Seek(byteOffset, SeekOrigin.Begin);
            fs.Write(raw, 0, raw.Length);
        }
    }

    public static byte[] BuildRawSector(uint absoluteLba, byte[] userData2048)
    {
        if (userData2048.Length != 2048)
            throw new ArgumentException("유저데이터는 2048바이트여야 합니다.", nameof(userData2048));

        var sector = new byte[2352];

        sector[0] = 0x00;

        for (int i = 1; i <= 10; i++) 
            sector[i] = 0xFF;

        sector[11] = 0x00;

        uint msf = absoluteLba + 150;

        sector[12] = ToBcd((byte)(msf / 75 / 60));
        sector[13] = ToBcd((byte)(msf / 75 % 60));
        sector[14] = ToBcd((byte)(msf % 75));
        sector[15] = 0x01;

        Buffer.BlockCopy(userData2048, 0, sector, 16, 2048);

        uint edc = ComputeEdc(sector, 0, 2064);

        sector[2064] = (byte)(edc & 0xFF);
        sector[2065] = (byte)((edc >> 8) & 0xFF);
        sector[2066] = (byte)((edc >> 16) & 0xFF);
        sector[2067] = (byte)((edc >> 24) & 0xFF);


        return sector;
    }

    private static byte ToBcd(byte value) => (byte)(((value / 10) << 4) | (value % 10));

    private static readonly uint[] EdcTable = BuildEdcTable();

    private static uint[] BuildEdcTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint edc = i;

            for (int j = 0; j < 8; j++)
                edc = (edc >> 1) ^ ((edc & 1) != 0 ? 0xD8018001 : 0);

            table[i] = edc;
        }

        return table;
    }

    private static uint ComputeEdc(byte[] data, int offset, int length)
    {
        uint edc = 0;

        for (int i = 0; i < length; i++)
            edc = EdcTable[(edc ^ data[offset + i]) & 0xFF] ^ (edc >> 8);

        return edc;
    }
}