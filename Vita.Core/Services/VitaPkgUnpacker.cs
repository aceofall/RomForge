using Vita.Core.Models;

namespace Vita.Core.Services;

public static class VitaPkgUnpacker
{
    public static string Unpack(string pkgPath, string license, string outputRoot, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var pkgStream = File.OpenRead(pkgPath);
        var header = VitaPkgDecryptor.ReadHeader(pkgStream);

        if (string.IsNullOrWhiteSpace(header.ContentId))
            throw new InvalidDataException("PKG에서 content_id를 읽을 수 없습니다.");

        byte[] klicensee = VitaPkgLicenseResolver.ResolveKlicensee(license, header.ContentId);

        if (!WorkBinReader.TryGetTitleIdFromContentId(header.ContentId, out string titleId))
            throw new InvalidDataException($"content_id에서 title id를 추출할 수 없습니다: {header.ContentId}");

        string extractDir;

        if (header.ContentType == (uint)VitaContentType.App)
        {
            VitaContentCategory category;

            using (var probe = new PkgSourceAccessor(pkgPath, license))
                category = VitaPkgCategoryResolver.ResolveAppOrPatch(probe);

            extractDir = category == VitaContentCategory.Patch ? Path.Combine(outputRoot, "patch", titleId) : Path.Combine(outputRoot, "app", titleId);
        }
        else if (header.ContentType == (uint)VitaContentType.Dlc)
        {
            extractDir = Path.Combine(outputRoot, "addcont", titleId, GetDlcSuffix(header.ContentId));
        }
        else
        {
            throw new NotSupportedException($"지원하지 않는 PKG content type: 0x{header.ContentType:x}");
        }

        VitaPkgDecryptor.ExtractTo(pkgStream, header, extractDir, progress, ct);

        string workBinPath = Path.Combine(extractDir, "sce_sys", "package", "work.bin");

        Directory.CreateDirectory(Path.GetDirectoryName(workBinPath)!);
        File.WriteAllBytes(workBinPath, VitaPkgLicenseResolver.BuildWorkBin(header.ContentId, klicensee));

        return extractDir;
    }

    private static string GetDlcSuffix(string contentId) => contentId.Length > 20 ? contentId[20..] : contentId;
}