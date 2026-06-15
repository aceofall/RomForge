using _3DS.Core.Crypto;
using _3DS.Core.Enums;
using _3DS.Core.IO;
using _3DS.Core.Models;
using _3DS.Core.Services;
using Common.WPF.ViewModels;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomForge.ViewModels._3DS;

public class InstallViewModel : ToolTabViewModel
{
    private static readonly HashSet<string> AllowedExtensions = [".3ds", ".cia", ".cci", ".zcci"];

    private bool _isInstalling;

    public ObservableCollection<TitleViewModel> Items { get; } = [];
    public Visibility HintVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool IsNotInstalling => !IsInstalling;

    public bool IsInstalling
    {
        get => _isInstalling;
        set { _isInstalling = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotInstalling)); }
    }

    public async Task AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            string fullPath = Path.GetFullPath(path);
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();

            if (!AllowedExtensions.Contains(ext) || Items.Any(f => f.FilePath == fullPath))
                continue;

            try
            {
                var vm = await ParseFile(fullPath);
                Items.Add(vm);
                OnPropertyChanged(nameof(HintVisibility));
            }
            catch { }
        }
    }

    private static async Task<TitleViewModel> ParseFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        InstalledTitle? title = null;
        SmdhInfo? smdhInfo = null;
        NcchHeader? ncchHeader = null;
        KeyStore keyStore = new();

        switch (ext)
        {
            case ".cia":
                {
                    await using var stream = File.OpenRead(path);
                    var reader = new CiaReader(keyStore);
                    var ciaSource = await reader.ParseAsync(stream);

                    await ciaSource.LoadGameInfoAsync();
                    smdhInfo = ciaSource.SmdhInfo;
                    ncchHeader = ciaSource.MainNcchHeader;
                    title = new InstalledTitle
                    {
                        Contents = [.. ciaSource.Contents],
                        TitleId = ciaSource.TmdHeader.TitleId.ToString("x16"),
                        Version = ciaSource.TmdHeader.Version,
                        ContentSize = (ulong)new FileInfo(path).Length,
                        ContentPath = path,
                        Type = (TitleType)(ciaSource.TmdHeader.TitleId >> 32)
                    };
                    break;
                }
            case ".cci":
            case ".3ds":
                {
                    await using var stream = File.OpenRead(path);

                    (ncchHeader, smdhInfo) = await ParseNcchFromCciStreamAsync(stream, keyStore);
                    title = TitleFromNcch(ncchHeader!, path);
                    break;
                }
            case ".zcci":
                {
                    await using var fileStream = File.OpenRead(path);
                    var z3dsHeader = Z3dsArchiveService.ParseZ3dsHeader(fileStream);
                    await using var decompStream = new ZcciDecompressStream(fileStream, z3dsHeader);

                    (ncchHeader, smdhInfo) = await ParseNcchFromCciStreamAsync(decompStream, keyStore);
                    title = TitleFromNcch(ncchHeader!, path);
                    break;
                }
        }

        TitleViewModel result = new()
        {
            Title = title!,
            FilePath = path,
            ProductCode = ncchHeader?.ProductCodeString ?? string.Empty,
            ShortDescription = smdhInfo?.ShortDescription ?? string.Empty,
            Publisher = smdhInfo?.Publisher ?? string.Empty,
            Crypto = !ncchHeader.NoCrypto
        };

        if (smdhInfo?.IconPixels is not null)
        {
            var bitmap = BitmapSource.Create(48, 48, 96, 96, PixelFormats.Bgr32, null, smdhInfo.IconPixels, 48 * 4);
            bitmap.Freeze();
            result.Icon = bitmap;
        }

        return result;
    }

    private static InstalledTitle TitleFromNcch(NcchHeader h, string path) => new()
    {
        TitleId = h.ProgramId.ToString("x16"),
        Version = h.Version,
        ContentSize = (ulong)new FileInfo(path).Length,
        ContentPath = path,
        Type = (TitleType)(h.ProgramId >> 32)
    };

    private static async Task<(NcchHeader ncchHeader, SmdhInfo? smdhInfo)> ParseNcchFromCciStreamAsync(Stream stream, KeyStore keyStore)
    {
        byte[] ncsdBuf = new byte[0x200];

        stream.Position = 0x100;

        await stream.ReadExactlyAsync(ncsdBuf);

        uint partOffset = BinaryPrimitives.ReadUInt32LittleEndian(ncsdBuf.AsSpan(0x20, 4));
        uint partSize = BinaryPrimitives.ReadUInt32LittleEndian(ncsdBuf.AsSpan(0x24, 4));
        long ncchOffset = partOffset * 0x200L;
        long ncchSize = partSize * 0x200L;
        byte[] ncchHeaderBuf = new byte[0x200];

        stream.Position = ncchOffset;

        await stream.ReadExactlyAsync(ncchHeaderBuf);

        var ncchHeader = NcchHeader.Parse(ncchHeaderBuf, 0);
        Stream ncchStream = new SubStream(stream, ncchOffset, ncchSize);

        if (!ncchHeader.NoCrypto)
            ncchStream = new NcchDecryptionStream(ncchStream, 0, keyStore);

        SmdhInfo? smdhInfo;
        await using (ncchStream)
            smdhInfo = await NcchGameInfoReader.LoadAsync(ncchStream);

        return (ncchHeader, smdhInfo);
    }

    public void RemoveItems(IEnumerable<TitleViewModel> items)
    {
        foreach (var item in items.ToList())
            Items.Remove(item);

        OnPropertyChanged(nameof(HintVisibility));
    }

    public void Clear()
    {
        Items.Clear();
        OnPropertyChanged(nameof(HintVisibility));
    }
}