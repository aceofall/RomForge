using Vita.Core.Cryptography;
using Vita.Core.Models;

namespace Vita.Core.Services;

public sealed class PkgSourceAccessor : IVitaSourceAccessor
{
    private const string WorkBinRelativePath = "sce_sys/package/work.bin";

    private readonly FileStream _stream;
    private readonly VitaAes128Ctr _ctr;
    private readonly Dictionary<string, VitaPkgItem> _items;
    private readonly byte[] _workBinBytes;

    public VitaPkgHeader Header { get; }

    public PkgSourceAccessor(string pkgPath, string license)
    {
        _stream = new FileStream(pkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Header = VitaPkgDecryptor.ReadHeader(_stream);
        _ctr = VitaPkgDecryptor.CreateCipher(Header);

        var items = VitaPkgDecryptor.ReadItemTable(_stream, Header, _ctr);
        var nonDirItems = items.Where(i => !VitaPkgDecryptor.IsDirectory(i)).ToList();
        var groups = nonDirItems.GroupBy(i => i.Name.Trim('/'), StringComparer.OrdinalIgnoreCase).ToList();
        var duplicates = groups.Where(g => g.Count() > 1).ToList();

        if (duplicates.Count > 0)
        {
            var detail = string.Join("\n", duplicates.Select(g =>
                $"key='{g.Key}' -> " + string.Join(" | ", g.Select(i => $"[Name='{i.Name}', DataOffset={i.DataOffset}, DataSize={i.DataSize}, Flags={i.Flags}]"))));

            throw new InvalidDataException($"PKG 아이템 테이블에 중복 이름이 있습니다 (전체 항목 {nonDirItems.Count}개):\n{detail}");
        }

        _items = groups.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        byte[] klicensee = VitaPkgLicenseResolver.ResolveKlicensee(license, Header.ContentId);

        _workBinBytes = VitaPkgLicenseResolver.BuildWorkBin(Header.ContentId, klicensee);
    }

    public bool DirectoryExists(string relativePath)
    {
        string prefix = Normalize(relativePath);

        if (prefix.Length == 0)
            return true;

        if (prefix.Equals("sce_sys/package", StringComparison.OrdinalIgnoreCase) || prefix.Equals("sce_sys", StringComparison.OrdinalIgnoreCase))
            return true;

        prefix += "/";

        return _items.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> EnumerateDirectoryNames(string relativePath)
    {
        string prefix = Normalize(relativePath);

        if (prefix.Length > 0)
            prefix += "/";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _items.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string rest = key[prefix.Length..];
            int slash = rest.IndexOf('/');

            if (slash > 0)
                names.Add(rest[..slash]);
        }

        return names;
    }

    public IEnumerable<string> EnumerateAllFiles() => _items.Keys.Append(WorkBinRelativePath);

    public bool FileExists(string relativePath)
    {
        string rel = Normalize(relativePath);

        return rel.Equals(WorkBinRelativePath, StringComparison.OrdinalIgnoreCase) || _items.ContainsKey(rel);
    }

    public byte[] ReadAllBytes(string relativePath)
    {
        string rel = Normalize(relativePath);

        if (rel.Equals(WorkBinRelativePath, StringComparison.OrdinalIgnoreCase))
            return _workBinBytes;

        if (!_items.TryGetValue(rel, out var item))
            throw new FileNotFoundException(relativePath);

        return VitaPkgDecryptor.DecryptItemData(_stream, Header, _ctr, item);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    public void Dispose()
    {
        _ctr.Dispose();
        _stream.Dispose();
    }
}