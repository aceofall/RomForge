using NSW.HacPack.Models;
using static LibHac.Ns.ApplicationControlProperty;

namespace NSW.M1.Core.Services;

public sealed record BuildRequest(string BaseFilePath, string UpdateFilePath, IReadOnlyList<string> DlcFilePaths, string PatchDir, string OutputDir)
{
    public Language Language;

    public bool HasPatch => !string.IsNullOrEmpty(PatchDir) && Directory.Exists(PatchDir);

    public GameMetadata? UserMetadata { get; set; }

    public uint? OverrideSdkVersion { get; set; }

    public byte? OverrideKeyGeneration { get; set; }

    public byte? TargetIdOffset { get; set; }
}