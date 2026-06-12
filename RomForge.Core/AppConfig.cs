using System.Text.Json;
using System.Text.Json.Serialization;

namespace Patch.Core;

public enum OutputMode { Normal, Arcade }

public class PatchConfig
{
    public OutputMode OutputMode   { get; set; } = OutputMode.Normal;
    public string?    OutputFolder { get; set; } = null;  // null = 원본 위치
}

public class AppConfig
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public PatchConfig Patch { get; set; } = new();

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath)) return this;
        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? this;
        }
        catch { return this; }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
