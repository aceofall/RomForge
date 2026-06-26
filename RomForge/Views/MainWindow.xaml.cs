using _3DS.Core.Crypto;
using _3DS.Core.Models;
using _3DS.Core.Services;
using NSW.WPF.UI;
using RomForge.Helpers;
using RomForge.ViewModels;
using System.Buffers.Binary;
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

        Loaded += async (_, _) =>
        {
            try
            {
                //await TestRepackAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"오류: {ex}");
                MessageBox.Show(ex.ToString());
            }
        };
    }

    private static async Task TestRepackAsync()
    {
        var keyStore = new KeyStore();
        await using var cciSource = await CciSource.OpenAsync(@"D:\3ds\Super Mario 3D Land.cci", keyStore);
        var ct = CancellationToken.None;

        var repackedNcchs = new Dictionary<int, byte[]>();

        foreach (var content in cciSource.Contents)
        {
            int idx = content.ContentIndex;
            var (ncchStream, _) = cciSource.OpenContentDecrypted(idx);
            await using (ncchStream)
            {
                // 파티션별 NCCH 헤더 읽기
                byte[] hdrBuf = new byte[NcchHeader.Size];
                await ncchStream.ReadExactlyAsync(hdrBuf, ct);
                var ncchHeader = NcchHeader.Parse(hdrBuf);
                ncchStream.Position = 0;

                var unpack = await NcchUnpacker.UnpackAsync(ncchStream, ncchHeader, ct);
                var exefsBlock = unpack.ExeFs != null ? ExeFsPacker.Pack(unpack.ExeFs.Files) : [];
                var romfsBlock = unpack.RomFs != null ? await RomFsPacker.PackAsync(ncchStream, unpack.RomFs, ct) : [];
                var ncchBlock = await NcchBuilder.BuildAsync(unpack, exefsBlock, romfsBlock, ct);

                repackedNcchs[idx] = ncchBlock;
                Debug.WriteLine($"파티션 {idx} 재패킹 완료: {ncchBlock.Length:X} bytes");
            }
        }

        // RepackedNcsdSource로 감싸서 NcsdBuilder에 넘기기
        var repackedSource = new RepackedNcsdSource(repackedNcchs, cciSource.Contents);
        string outputPath = @"D:\3ds\Super Mario 3D Land_repacked.cci";
        await using var output = File.OpenWrite(outputPath);
        await NcsdBuilder.BuildAsync(repackedSource, output, null, ct);

        Debug.WriteLine($"완료: {outputPath}");
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