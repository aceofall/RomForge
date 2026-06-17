using NSW.Core.Models;

namespace RomForge.Core.Models.Switch;

public sealed class TitleGroup(string baseTitleId, string baseTitleName)
{
    public string BaseTitleId { get; } = baseTitleId;

    public string BaseTitleName { get; } = baseTitleName;

    public List<MetadataResult> BaseMetas { get; } = [];

    public List<MetadataResult> PatchMetas { get; } = [];

    public List<MetadataResult> DlcMetas { get; } = [];
}