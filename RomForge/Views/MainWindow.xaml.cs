using _3DS.Core.Models;
using _3DS.Core.Services;
using NSW.WPF.UI;
using RomForge.Helpers;
using RomForge.ViewModels;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
                await TestExeFsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"예외: {ex}");
                MessageBox.Show(ex.ToString());
            }
        };
    }

    private static async Task TestExeFsAsync()
    {
        var keyStore = new _3DS.Core.Crypto.KeyStore();
        await using var cciSource = await CciSource.OpenAsync(@"D:\3ds\Super Mario 3D Land.cci", keyStore);
        var ct = CancellationToken.None;

        var (ncchStream, _) = cciSource.OpenContentDecrypted(0);
        await using (ncchStream)
        {
            var unpack = await RomFsUnpacker.UnpackAsync(ncchStream, cciSource.MainHeader, ct);
            byte[] repacked = await RomFsPacker.PackAsync(ncchStream, unpack, ct);

            // 원본 RomFS 블록
            long romfsOffset = (long)cciSource.MainHeader.RomfsOffset * 0x200;
            byte[] original = new byte[repacked.Length];
            ncchStream.Position = romfsOffset;
            await ncchStream.ReadExactlyAsync(original, ct);

            ulong realOff3 = 0x1000;

            int l3Base = (int)realOff3;
            var origRomfs = RomFsHeader.Parse(original, l3Base);


            int dirEntryBase = l3Base + (int)origRomfs.DirEntryOffset;

            int entryBase = dirEntryBase + 0xA4;
            Debug.WriteLine($"parentOffset    orig={BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entryBase + 0x00)):X8} mine={BinaryPrimitives.ReadUInt32LittleEndian(repacked.AsSpan(entryBase + 0x00)):X8}");
            Debug.WriteLine($"siblingOffset   orig={BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entryBase + 0x04)):X8} mine={BinaryPrimitives.ReadUInt32LittleEndian(repacked.AsSpan(entryBase + 0x04)):X8}");
            Debug.WriteLine($"childDirOffset  orig={BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entryBase + 0x08)):X8} mine={BinaryPrimitives.ReadUInt32LittleEndian(repacked.AsSpan(entryBase + 0x08)):X8}");
            Debug.WriteLine($"childFileOffset orig={BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entryBase + 0x0C)):X8} mine={BinaryPrimitives.ReadUInt32LittleEndian(repacked.AsSpan(entryBase + 0x0C)):X8}");
            Debug.WriteLine($"hashSibling     orig={BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entryBase + 0x10)):X8} mine={BinaryPrimitives.ReadUInt32LittleEndian(repacked.AsSpan(entryBase + 0x10)):X8}");
            Debug.WriteLine($"nameSize        orig={BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entryBase + 0x14)):X8} mine={BinaryPrimitives.ReadUInt32LittleEndian(repacked.AsSpan(entryBase + 0x14)):X8}");
        }
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