using Vita.Core.Models;

namespace Vita.Core.Services;

public static class VitaPkgCategoryResolver
{
    public static VitaContentCategory ResolveAppOrPatch(IVitaSourceAccessor source)
    {
        try
        {
            byte[] sfoBytes = source.ReadAllBytes("sce_sys/param.sfo");
            var sfo = VitaSfoParser.Parse(sfoBytes);
            string? category = VitaSfoParser.GetString(sfo, "CATEGORY");

            return string.Equals(category, "gp", StringComparison.OrdinalIgnoreCase) ? VitaContentCategory.Patch : VitaContentCategory.App;
        }
        catch
        {
            return VitaContentCategory.App;
        }
    }
}