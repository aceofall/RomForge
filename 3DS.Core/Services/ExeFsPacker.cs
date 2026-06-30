using _3DS.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace _3DS.Core.Services;

public static class ExeFsPacker
{
    private const int BlockSize = 0x200;
    private const int MaxEntries = 8;

    public static byte[] Pack(IReadOnlyList<ExeFsFile> files)
    {
        if (files.Count == 0)
            throw new ArgumentException("ExeFS에 파일이 없습니다.");
        if (files.Count > MaxEntries)
            throw new ArgumentException($"ExeFS 최대 파일 수({MaxEntries}) 초과");

        uint totalSize = ExeFsHeader.Size;

        foreach (var f in files)
            totalSize += AlignUp((uint)f.Data.Length, BlockSize);

        byte[] buf = new byte[totalSize];

        uint currentOffset = 0;

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];

            if (i > 0)
                currentOffset = AlignUp(currentOffset + (uint)files[i - 1].Data.Length, BlockSize);

            int entryBase = i * 0x10;
            byte[] nameBytes = Encoding.ASCII.GetBytes(file.Name);

            Array.Copy(nameBytes, 0, buf, entryBase, Math.Min(nameBytes.Length, 8));
            BitConverter.GetBytes(currentOffset).CopyTo(buf, entryBase + 8);
            BitConverter.GetBytes((uint)file.Data.Length).CopyTo(buf, entryBase + 12);

            int hashBase = 0x100 + (MaxEntries - 1 - i) * 0x20;

            SHA256.HashData(file.Data).CopyTo(buf, hashBase);
        }

        currentOffset = 0;
        for (int i = 0; i < files.Count; i++)
        {
            if (i > 0)
                currentOffset = AlignUp(currentOffset + (uint)files[i - 1].Data.Length, BlockSize);

            int dataPos = ExeFsHeader.Size + (int)currentOffset;

            files[i].Data.CopyTo(buf, dataPos);
        }

        return buf;
    }

    public static async Task<byte[]> PackFromDirectoryAsync(string exefsDir, CancellationToken ct = default)
    {
        var fileInfos = Directory.GetFiles(exefsDir, "*.bin")
            .Select(path =>
            {
                string baseName = Path.GetFileNameWithoutExtension(path);
                string exefsName = baseName == "code" ? ".code" : baseName;

                return (path, exefsName);
            })
            .OrderBy(x => x.exefsName == ".code" ? 0 : 1)
            .ThenBy(x => x.exefsName)
            .ToList();

        var files = new List<ExeFsFile>();

        foreach (var (path, name) in fileInfos)
        {
            byte[] data = await File.ReadAllBytesAsync(path, ct);

            files.Add(new ExeFsFile
            {
                Name = name,
                Data = data,
                ExpectedHash = [],
                HashValid = false,
            });
        }

        return Pack(files);
    }

    public static async Task<byte[]> PackWithPatchAsync(IReadOnlyList<ExeFsFile> originalFiles, string? exefsPatchDir, CancellationToken ct = default)
    {
        if (exefsPatchDir == null || !Directory.Exists(exefsPatchDir))
            return Pack(originalFiles);

        var patchedFiles = new List<ExeFsFile>();

        foreach (var file in originalFiles)
        {
            string fileName = file.Name == ".code" ? "code.bin" : file.Name + ".bin";
            string patchPath = Path.Combine(exefsPatchDir, fileName);

            if (File.Exists(patchPath))
            {
                byte[] patchData = await File.ReadAllBytesAsync(patchPath, ct);

                patchedFiles.Add(new ExeFsFile
                {
                    Name = file.Name,
                    Data = patchData,
                    ExpectedHash = [],
                    HashValid = false,
                });
            }
            else
            {
                patchedFiles.Add(file);
            }
        }

        return Pack(patchedFiles);
    }

    private static uint AlignUp(uint v, uint a) => (v + a - 1) & ~(a - 1);
}