using System.Text.Json;

namespace RomZip.Core.Services;

public class SwitchConfig
{
    public int CompressLevel { get; set; } = 18;
    public bool VerifyCompress { get; set; } = false;
    public bool UseBlockMode { get; set; } = true;
    public bool UseBlocklessMode { get; set; } = false;
}

public class AzaharConfig
{
    public int CompressLevel { get; set; } = 18;
}

public class DolphinConfig
{
    public int CompressLevel { get; set; } = 18;
}

public class AppConfig
{
    #region Fields

    private static readonly string DefaultFilePath =
        Path.ChangeExtension(Environment.ProcessPath!, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    #endregion

    #region Properties

    public SwitchConfig Switch { get; set; } = new();
    public AzaharConfig Azahar { get; set; } = new();
    public DolphinConfig Dolphin { get; set; } = new();

    #endregion

    #region Public Methods

    public AppConfig Load()
    {
        if (!File.Exists(DefaultFilePath))
        {
            Save();
            return this;
        }

        try
        {
            var json = File.ReadAllText(DefaultFilePath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json);
            if (loaded is null) return this;

            Switch = loaded.Switch ?? new();
            Azahar = loaded.Azahar ?? new();
            Dolphin = loaded.Dolphin ?? new();

            if (!Switch.UseBlockMode && !Switch.UseBlocklessMode)
                Switch.UseBlockMode = true;
        }
        catch
        {
            Save();
        }

        return this;
    }

    public void Save()
    {
        File.WriteAllText(DefaultFilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    #endregion
}