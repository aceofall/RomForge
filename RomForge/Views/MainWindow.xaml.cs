using _3DS.Core.Crypto;
using _3DS.Core.Models;
using _3DS.Core.Services;
using Common;
using NSW.WPF.UI;
using RomForge.Helpers;
using RomForge.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace RomForge.Views;

public partial class MainWindow : Window
{

    private MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        Closing += MainWindow_Closing;

        //Loaded += async (_, _) =>
        //{
        //    try
        //    {
        //        string fileName = @"D:\3ds\BRAVELY DEFAULT.cci";
        //        await TestRepackAsync(fileName);
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"오류: {ex}");
        //        MessageBox.Show(ex.ToString());
        //    }
        //};
    }

    private static async Task TestRepackAsync(string fileName)
    {
        var keyStore = new KeyStore();
        await using var cciSource = await CciSource.OpenAsync(fileName, keyStore);
        var ct = CancellationToken.None;
        var repackedNcchs = new Dictionary<int, (NcchUnpackResult unpack, byte[] exefsBlock, Stream ncchSource, RomFsUnpackResult? romFs)>();

        var progressLog = new Progress<ProgressInfo>(info => {
            Debug.WriteLine($"[{info.Label}] {info.Percent}% - {info.Speed} - {info.TimeInfo}");
        });        

        foreach (var content in cciSource.Contents)
        {
            int idx = content.ContentIndex;
            var (ncchStream, _) = await cciSource.OpenContentDecrypted(idx);

            byte[] hdrBuf = new byte[NcchHeader.Size];
            await ncchStream.ReadExactlyAsync(hdrBuf, ct);
            var ncchHeader = NcchHeader.Parse(hdrBuf);
            ncchStream.Position = 0;

            var unpack = await NcchUnpacker.UnpackAsync(ncchStream, ncchHeader, ct);
            var exefsBlock = unpack.ExeFs != null ? ExeFsPacker.Pack(unpack.ExeFs.Files) : [];

            repackedNcchs[idx] = (unpack, exefsBlock, ncchStream, unpack.RomFs);
            Debug.WriteLine($"파티션 {idx} 준비 완료");
        }

        var repackedSource = await RepackedNcsdSource.CreateAsync(repackedNcchs, cciSource.Contents);
        string outputPath = fileName + "re.cci";
        await using var output = File.Open(outputPath, FileMode.Create, FileAccess.ReadWrite);

        long totalCciSize = NcsdBuilder.CalculateOutputSize(repackedSource);
        var reporter = new ProgressReporter("젤다", "AA", totalCciSize, progressLog);

        await NcsdBuilder.BuildAsync(repackedSource, output, reporter.CreateAction(), ct);

        Debug.WriteLine($"완료: {outputPath}");
    }

    private static async Task CompareUnpackedAsync(string fileName)
    {
        var keyStore = new KeyStore();
        var ct = CancellationToken.None;

        await using var original = await CciSource.OpenAsync(fileName, keyStore);
        await using var repacked = await CciSource.OpenAsync(fileName + "re.cci", keyStore);

        foreach (var content in original.Contents)
        {
            int idx = content.ContentIndex;
            Debug.WriteLine($"\n=== 파티션 {idx} ===");

            var (origStream, _) = await original.OpenContentDecrypted(idx);
            await using (origStream)
            {
                byte[] hdrBuf = new byte[NcchHeader.Size];
                await origStream.ReadExactlyAsync(hdrBuf, ct);
                var ncchHeader = NcchHeader.Parse(hdrBuf);
                origStream.Position = 0;
                var origUnpack = await NcchUnpacker.UnpackAsync(origStream, ncchHeader, ct);

                var (repStream, _) = await repacked.OpenContentDecrypted(idx);
                await using (repStream)
                {
                    byte[] hdrBuf2 = new byte[NcchHeader.Size];
                    await repStream.ReadExactlyAsync(hdrBuf2, ct);
                    var ncchHeader2 = NcchHeader.Parse(hdrBuf2);
                    repStream.Position = 0;
                    var repUnpack = await NcchUnpacker.UnpackAsync(repStream, ncchHeader2, ct);

                    // ExHeader 비교
                    if (origUnpack.ExHeader != null && repUnpack.ExHeader != null)
                    {
                        bool exhdrMatch = origUnpack.ExHeader.AsSpan().SequenceEqual(repUnpack.ExHeader.AsSpan());
                        Debug.WriteLine($"ExHeader: {(exhdrMatch ? "OK" : "MISMATCH")}");
                    }

                    // ExeFS 비교
                    if (origUnpack.ExeFs != null && repUnpack.ExeFs != null)
                    {
                        foreach (var origFile in origUnpack.ExeFs.Files)
                        {
                            var repFile = repUnpack.ExeFs.Files.FirstOrDefault(f => f.Name == origFile.Name);
                            if (repFile == null)
                            {
                                Debug.WriteLine($"ExeFS/{origFile.Name}: MISSING");
                                continue;
                            }
                            bool match = origFile.Data.AsSpan().SequenceEqual(repFile.Data.AsSpan());
                            Debug.WriteLine($"ExeFS/{origFile.Name}: {(match ? "OK" : "MISMATCH")} (orig={origFile.Data.Length:X}, rep={repFile.Data.Length:X})");
                        }
                    }

                    // RomFS 비교
                    if (origUnpack.RomFs != null && repUnpack.RomFs != null)
                    {
                        var origFiles = origUnpack.RomFs.Files;
                        var repFiles = repUnpack.RomFs.Files;

                        Debug.WriteLine($"RomFS 파일 수: orig={origFiles.Count}, rep={repFiles.Count}");

                        long origDataBase = origUnpack.RomFs.DataLevel2Offset + origUnpack.RomFs.RomFsHeader.DataOffset;
                        long repDataBase = repUnpack.RomFs.DataLevel2Offset + repUnpack.RomFs.RomFsHeader.DataOffset;

                        foreach (var origFile in origFiles)
                        {
                            var repFile = repFiles.FirstOrDefault(f => f.FullPath == origFile.FullPath);
                            if (repFile == null)
                            {
                                Debug.WriteLine($"RomFS {origFile.FullPath}: MISSING");
                                continue;
                            }

                            if (origFile.DataSize != repFile.DataSize)
                            {
                                Debug.WriteLine($"RomFS {origFile.FullPath}: SIZE MISMATCH (orig={origFile.DataSize:X}, rep={repFile.DataSize:X})");
                                continue;
                            }

                            if (origFile.DataSize == 0)
                            {
                                Debug.WriteLine($"RomFS {origFile.FullPath}: OK (empty)");
                                continue;
                            }

                            byte[] origData = new byte[origFile.DataSize];
                            byte[] repData = new byte[repFile.DataSize];

                            origStream.Position = origDataBase + (long)origFile.DataOffset;
                            await origStream.ReadExactlyAsync(origData, ct);

                            repStream.Position = repDataBase + (long)repFile.DataOffset;
                            await repStream.ReadExactlyAsync(repData, ct);

                            bool match = origData.AsSpan().SequenceEqual(repData.AsSpan());
                            Debug.WriteLine($"RomFS {origFile.FullPath}: {(match ? "OK" : "MISMATCH")} ({origFile.DataSize:X} bytes)");
                        }
                    }
                }
            }
        }

        Debug.WriteLine("\n비교 완료!");
    }



    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        int value = 1;

        _ = Win32API.DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        ViewModel.SaveConfig();
        bool busy = ViewModel.CompressVM.IsLocked || ViewModel.PatchVM.IsLocked;

        if (!busy)
            return;

        var result = MessageBoxHelper.ShowQuestion("작업이 진행 중입니다. 취소하고 종료할까요?");

        if (result)
        {
            ViewModel.CompressVM.CancelCommand.Execute(null);
            ViewModel.PatchVM.CancelCommand.Execute(null);
        }
        else
            e.Cancel = true;
    }
}