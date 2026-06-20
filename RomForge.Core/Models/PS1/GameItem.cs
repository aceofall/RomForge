using System.Text.Json.Serialization;

namespace RomForge.Core.Models.PS1;

public class GameItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("pic0")]
    public string Pic0 { get; set; } = string.Empty;

    [JsonPropertyName("pic1")]
    public string Pic1 { get; set; } = string.Empty;
}