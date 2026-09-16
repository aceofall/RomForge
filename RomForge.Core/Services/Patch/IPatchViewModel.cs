namespace RomForge.Core.Services.Patch;

public interface IPatchViewModel
{
    string? SourcePath { get; }

    Task RunAsync();

    bool CanRun();

    void Cancel();

    void Clear();
}