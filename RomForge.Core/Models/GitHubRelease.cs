using System.Text.Json.Serialization;

namespace RomForge.Core.Models
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}