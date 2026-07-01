using System.Text.Json.Serialization;

namespace RomForge.Core.Models.PS;

public class GameItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("eTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ETitle { get; set; }

    [JsonPropertyName("lTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LTitle { get; set; }

    [JsonPropertyName("languages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Languages { get; set; }

    [JsonPropertyName("pic0")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pic0 { get; set; }

    [JsonPropertyName("pic1")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pic1 { get; set; }
}