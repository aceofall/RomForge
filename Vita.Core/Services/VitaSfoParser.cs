using System.Buffers.Binary;
using System.Text;

namespace Vita.Core.Services;

public static class VitaSfoParser
{
    private const uint Magic = 0x00505346;

    public static Dictionary<string, byte[]> Parse(byte[] data)
    {
        if (data.Length < 20)
            throw new InvalidDataException("SFO 파일 크기가 너무 작습니다.");

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(data);

        if (magic != Magic)
            throw new InvalidDataException("SFO magic이 올바르지 않습니다.");

        uint keyTableStart = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        uint dataTableStart = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < entryCount; i++)
        {
            int entryOffset = 20 + i * 16;
            ushort keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryOffset));
            uint dataLen = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryOffset + 4));
            uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryOffset + 12));
            int keyStart = (int)(keyTableStart + keyOffset);
            int keyEnd = keyStart;

            while (keyEnd < data.Length && data[keyEnd] != 0)
                keyEnd++;

            string key = Encoding.ASCII.GetString(data, keyStart, keyEnd - keyStart);
            int valueStart = (int)(dataTableStart + dataOffset);
            byte[] value = data.AsSpan(valueStart, (int)dataLen).ToArray();

            result[key] = value;
        }

        return result;
    }

    public static string? GetString(Dictionary<string, byte[]> sfo, string key)
    {
        if (!sfo.TryGetValue(key, out var value))
            return null;

        int nul = Array.IndexOf(value, (byte)0);

        return Encoding.UTF8.GetString(value, 0, nul < 0 ? value.Length : nul);
    }
}