using Common;
using Common.WPF.ViewModels;
using Patch.Core;
using RomForge.Core;
using RomForge.Core.Services;
using RomForge.Helpers;
using RomForge.Models;
using RomZip.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace RomForge.ViewModels;

public class PatchViewModel : ToolTabViewModel
{
    private readonly Core.AppConfig _config;

    public NormalPatchViewModel NormalVM { get; }

    public ArcadePatchViewModel ArcadeVM { get; } = new();

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = [];    

    public ICommand RunCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ClearCommand { get; }

    private CancellationTokenSource? _cts;

    public PatchViewModel(Core.AppConfig config)
    {
        _config = config;
        NormalVM = new NormalPatchViewModel(_config);
        RunCommand = new RelayCommand(async _ => await RunAsync());
        CancelCommand = new RelayCommand(_ => _cts?.Cancel());
        ClearCommand = new RelayCommand(_ => Clear());
    }

    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();

        using (BeginWork())
        {
            switch (SelectedTabIndex)
            {
                case 0: await RunNormalAsync(_cts.Token); break;
                case 1: await RunArcadeAsync(_cts.Token); break;
            }
        }
    }

    private async Task RunNormalAsync(CancellationToken ct)
    {
        if (NormalVM.SourcePath is null || NormalVM.PatchPath is null) return;

        NormalVM.Progress = 0;
        NormalVM.StatusText = "패치 준비 중...";
        NormalVM.StatusColor = "#888888";

        try
        {
            var sourceBytes = await File.ReadAllBytesAsync(NormalVM.SourcePath, ct);
            var patchBytes = await File.ReadAllBytesAsync(NormalVM.PatchPath, ct);
            var result = await Task.Run(() => UniversalPatcher.ApplyPatch(sourceBytes, patchBytes, p => NormalVM.Progress = (int)(p * 100)), ct);

            // 1. 결과물을 저장할 임시 경로
            string tempOutputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");

            // 2. 압축 여부에 따른 최종 파일 경로 설정
            string finalPath = NormalVM.SourcePath;
            string backupPath = Path.ChangeExtension(NormalVM.SourcePath, ".bak");

            if (NormalVM.AutoCompress)
            {
                NormalVM.StatusText = "압축 중...";
                var detected = FormatDetector.Detect(NormalVM.SourcePath);

                // 미지원 포맷이거나 포맷을 알 수 없는 경우 무조건 ZIP
                if (detected.Format == RomZip.Core.Enums.RomFormat.Unknown)
                {
                    finalPath = Path.ChangeExtension(NormalVM.SourcePath, ".zip");
                    await Task.Run(() =>
                    {
                        using var zipStream = new FileStream(tempOutputPath, FileMode.Create);
                        using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create);
                        var entry = archive.CreateEntry(Path.GetFileNameWithoutExtension(NormalVM.SourcePath) + ".patched" + Path.GetExtension(NormalVM.SourcePath));
                        using var entryStream = entry.Open();
                        entryStream.Write(result, 0, result.Length);
                    }, ct);
                }
                else
                {
                    // 지원 포맷일 경우: 여기서는 그대로 저장하거나, 
                    // 원하신다면 해당 포맷에 맞는 압축 로직을 추가하시면 됩니다.
                    await File.WriteAllBytesAsync(tempOutputPath, result, ct);
                }
            }
            else
            {
                await File.WriteAllBytesAsync(tempOutputPath, result, ct);
            }

            // 3. 기존 백업 파일이 있다면 삭제 (File.Replace 오류 방지)
            if (File.Exists(backupPath)) File.Delete(backupPath);

            // 4. 안전한 교체 (임시 파일 -> 원본 경로, 원본 -> 백업)
            File.Replace(tempOutputPath, NormalVM.SourcePath, backupPath);

            NormalVM.Progress = 100;
            NormalVM.StatusText = "완료";
            NormalVM.StatusColor = "#4CAF50";
            Log($"패치 완료: {Path.GetFileName(finalPath)}", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            NormalVM.StatusText = $"실패: {ex.Message}";
            NormalVM.StatusColor = "#F44336";
            Log($"패치 실패: {ex.Message}", LogLevel.Error);
        }
    }

    private async Task RunArcadeAsync(CancellationToken ct)
    {
        var matched = ArcadeVM.MatchItems.Where(x => x.IsMatched).ToList();
        if (matched.Count == 0) return;

        foreach (var item in matched)
        {
            item.Progress = 0;
            item.Status = string.Empty;
        }

        await Parallel.ForEachAsync(matched,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (item, token) =>
            {
                try
                {
                    await PatchService.ApplyAsync(
                        item.SourcePath, item.PatchPath!,
                        new Progress<ProgressInfo>(p =>
                        {
                            item.Progress = p.Percent;
                            ArcadeVM.UpdateTotalProgress();
                        }),
                        null, token);

                    item.Progress = 100;
                    item.Status = "완료";
                    item.StatusColor = "#4CAF50";
                }
                catch (OperationCanceledException)
                {
                    item.Status = "취소";
                    item.StatusColor = "#888888";
                }
                catch
                {
                    item.Status = "실패";
                    item.StatusColor = "#F44336";
                }
                finally
                {
                    ArcadeVM.UpdateTotalProgress();
                }
            });
    }

    private void Clear()
    {
        switch (SelectedTabIndex)
        {
            case 0: NormalVM.Clear(); break;
            case 1: ArcadeVM.Clear(); break;
        }
    }

    private void Log(string message, LogLevel level)
    {
        App.Current.Dispatcher.Invoke(() =>
            LogEntries.Add(new LogEntry { Message = message, Level = level }));
    }
}