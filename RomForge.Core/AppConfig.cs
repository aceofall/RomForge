using Common.WPF.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace RomForge.Core;

public class SwitchConfig : ViewModelBase
{
    private int _compressLevel = 18;
    public int CompressLevel { get => _compressLevel; set => SetProperty(ref _compressLevel, value); }

    private bool _verifyCompress = false;
    public bool VerifyCompress { get => _verifyCompress; set => SetProperty(ref _verifyCompress, value); }

    private bool _useBlockMode = true;
    public bool UseBlockMode { get => _useBlockMode; set => SetProperty(ref _useBlockMode, value); }

    private bool _useBlocklessMode = false;
    public bool UseBlocklessMode { get => _useBlocklessMode; set => SetProperty(ref _useBlocklessMode, value); }

    private bool _forceKeyGen0 = false;
    public bool ForceKeyGen0 { get => _forceKeyGen0; set => SetProperty(ref _forceKeyGen0, value); }
}

public class AzaharConfig : ViewModelBase
{
    private int _compressLevel = 18;
    public int CompressLevel { get => _compressLevel; set => SetProperty(ref _compressLevel, value); }
}

public class DolphinConfig : ViewModelBase
{
    private int _compressLevel = 18;
    public int CompressLevel { get => _compressLevel; set => SetProperty(ref _compressLevel, value); }
}

public class PatchConfig : ViewModelBase
{
    private bool _autoCompress;
    public bool AutoCompress
    {
        get => _autoCompress;
        set { SetProperty(ref _autoCompress, value); }
    }
}

public class AppConfig : ViewModelBase
{
    private static readonly string DefaultFilePath = Path.ChangeExtension(Environment.ProcessPath!, "config.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private SwitchConfig _switch = new();
    public SwitchConfig Switch { get => _switch; set => SetProperty(ref _switch, value); }

    private AzaharConfig _azahar = new();
    public AzaharConfig Azahar { get => _azahar; set => SetProperty(ref _azahar, value); }

    private DolphinConfig _dolphin = new();
    public DolphinConfig Dolphin { get => _dolphin; set => SetProperty(ref _dolphin, value); }

    private PatchConfig _patch = new();
    public PatchConfig Patch { get => _patch; set => SetProperty(ref _patch, value); }

    public AppConfig Load()
    {
        if (!File.Exists(DefaultFilePath)) { Save(); return this; }
        try
        {
            var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(DefaultFilePath));
            if (loaded != null)
            {
                Switch = loaded.Switch ?? new();
                Azahar = loaded.Azahar ?? new();
                Dolphin = loaded.Dolphin ?? new();
                Patch = loaded.Patch ?? new();
            }

            SubscribeToChanges();
        }
        catch { Save(); }
        return this;
    }

    private void SubscribeToChanges()
    {
        void AutoSave(object? s, PropertyChangedEventArgs e) => Save();

        Switch.PropertyChanged += AutoSave;
        Azahar.PropertyChanged += AutoSave;
        Dolphin.PropertyChanged += AutoSave;
        Patch.PropertyChanged += AutoSave;
    }

    public void Save() => File.WriteAllText(DefaultFilePath, JsonSerializer.Serialize(this, JsonOptions));
}