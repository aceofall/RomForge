
using Vita.Core.Models;

namespace Vita.Core.Services;

public static class VitaPkgProbe
{
    public static VitaPkgProbeResult Probe(string pkgPath)
    {
        using var accessor = new PkgSourceAccessor(pkgPath);

        if (!WorkBinReader.TryGetTitleIdFromContentId(accessor.Header.ContentId, out string titleId))
            throw new InvalidDataException($"content_id에서 title id를 추출할 수 없습니다: {accessor.Header.ContentId}");

        return accessor.Header.ContentType switch
        {
            (uint)VitaContentType.App => new VitaPkgProbeResult
            {
                TitleId = titleId,
                Category = VitaPkgCategoryResolver.ResolveAppOrPatch(accessor),
                ContentId = accessor.Header.ContentId
            },
            (uint)VitaContentType.Dlc => new VitaPkgProbeResult
            {
                TitleId = titleId,
                Category = VitaContentCategory.Addcont,
                ContentIdSuffix = accessor.Header.ContentId.Length > 20 ? accessor.Header.ContentId[20..] : accessor.Header.ContentId,
                ContentId = accessor.Header.ContentId
            },
            _ => throw new NotSupportedException($"지원하지 않는 PKG content type: 0x{accessor.Header.ContentType:x}")
        };
    }
} 