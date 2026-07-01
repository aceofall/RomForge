using Patch.Core.Formats.DCP.Models;
using System.Text;

namespace Patch.Core.Formats.DCP.Services;

public static class Iso9660DirectoryReader
{
    private const int PvdSector = 16;

    public static Iso9660Entry ReadTree(Func<uint, byte[]> sectorReader, uint pvdLba = 16)
    {
        var pvd = sectorReader(pvdLba);

        if (pvd[0] != 0x01 || pvd[1] != 'C' || pvd[2] != 'D' || pvd[3] != '0' || pvd[4] != '0' || pvd[5] != '1')
            throw new InvalidDataException("유효한 ISO9660 PVD를 찾을 수 없습니다.");

        var rootLba = BitConverter.ToUInt32(pvd, 156 + 2);
        var rootSize = BitConverter.ToUInt32(pvd, 156 + 10);

        var root = new Iso9660Entry
        {
            Name = string.Empty,
            Lba = rootLba,
            Size = rootSize,
            IsDirectory = true,
            FullPath = string.Empty
        };

        ReadDirectory(sectorReader, root);

        return root;
    }

    private static void ReadDirectory(Func<uint, byte[]> sectorReader, Iso9660Entry dir)
    {
        int sectorsToRead = (int)Math.Ceiling(dir.Size / 2048.0);

        for (int s = 0; s < sectorsToRead; s++)
        {
            var sector = sectorReader(dir.Lba + (uint)s);
            var pos = 0;

            while (pos < sector.Length)
            {
                var recordLen = sector[pos];

                if (recordLen == 0)
                {
                    break;
                }

                var flags = sector[pos + 25];
                var isDir = (flags & 0x02) != 0;
                var fileLba = BitConverter.ToUInt32(sector, pos + 2);
                var fileSize = BitConverter.ToUInt32(sector, pos + 10);
                var nameLen = sector[pos + 32];
                var rawName = Encoding.ASCII.GetString(sector, pos + 33, nameLen);

                bool isSelfOrParent = nameLen == 1 && (rawName[0] == '\0' || rawName[0] == '\u0001');

                if (!isSelfOrParent)
                {
                    var name = isDir ? rawName : rawName.Split(';')[0];
                    var childPath = string.IsNullOrEmpty(dir.FullPath) ? name : $"{dir.FullPath}/{name}";

                    var entry = new Iso9660Entry
                    {
                        Name = name,
                        Lba = fileLba,
                        Size = fileSize,
                        IsDirectory = isDir,
                        FullPath = childPath
                    };

                    if (isDir)
                        ReadDirectory(sectorReader, entry);

                    dir.Children.Add(entry);
                }

                pos += recordLen;
            }
        }
    }

    public static byte[] ReadFile(Func<uint, byte[]> sectorReader, Iso9660Entry file)
    {
        if (file.IsDirectory)
            throw new InvalidOperationException($"디렉토리는 ReadFile로 읽을 수 없습니다: {file.FullPath}");

        var buffer = new byte[file.Size];
        int sectorsToRead = (int)Math.Ceiling(file.Size / 2048.0);

        for (int s = 0; s < sectorsToRead; s++)
        {
            var sector = sectorReader(file.Lba + (uint)s);
            int offset = s * 2048;
            int copyLen = (int)Math.Min(2048, file.Size - offset);

            Buffer.BlockCopy(sector, 0, buffer, offset, copyLen);
        }

        return buffer;
    }

    public static IEnumerable<Iso9660Entry> Flatten(Iso9660Entry root)
    {
        foreach (var child in root.Children)
        {
            if (child.IsDirectory)
            {
                foreach (var descendant in Flatten(child))
                    yield return descendant;
            }
            else
            {
                yield return child;
            }
        }
    }
}