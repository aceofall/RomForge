using Common;
using PBP.Core.Models;

namespace PBP.Core.Services;

public static class PbpPackager
{
    public static Task<string> WriteSingleDiscAsync(string inputPath, string gameTitle, int compressionLevel, IProgress<ProgressInfo> progress, Action<string, LogLevel, string> log, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var ext = Path.GetExtension(inputPath).ToLowerInvariant();
            var isoPath = ext == ".cue" ? CueFileResolver.GetBinPath(inputPath) : inputPath;
            var outputPath = Path.ChangeExtension(inputPath, ".pbp");
            var disc = ext == ".cue" ? DiskSource.FromBinCue(isoPath, inputPath) : DiskSource.FromIso(isoPath);
            var gameId = GameIdReader.ReadFromDisk(disc);

            log($"[{gameId}] GameID 인식됨", LogLevel.Info, gameId);

            var assets = new PbpAssets();
            var basePbpBytes = BaseResourceLoader.GetBasePbpBytes();

            PbpHeaderBuilder.EnsureRequiredAssets(assets, basePbpBytes);

            var sfo = BuildDefaultSfo(gameId, gameTitle);
            var header = PbpHeaderBuilder.BuildHeader(assets, sfo.Size);
            var psarOffset = header[9];
            using var outputStream = new FileStream(outputPath, FileMode.Create);

            WriteCommonSections(outputStream, header, sfo, assets, psarOffset);

            var isoSize = (uint)new FileInfo(isoPath).Length;
            var tocData = TocBuilder.BuildSingleTrackToc(isoSize);

            using var isoStream = new FileStream(isoPath, FileMode.Open, FileAccess.Read);

            SingleDiscPsarWriter.WritePsar(outputStream, new DiscWriteInfo(isoStream, isoSize, gameId, gameTitle, tocData), psarOffset, compressionLevel, ct, (cur, total) => progress.Report(new ProgressInfo { Percent = (int)(cur * 100.0 / total) }));
            StartDatWriter.WriteStartDat(outputStream, basePbpBytes, assets.BootPng);

            log($"완료: {Path.GetFileName(outputPath)}", LogLevel.Ok, gameId);

            return outputPath;
        }, ct);
    }

    public static Task<string> WriteMultiDiscAsync(IReadOnlyList<(string IsoPath, string GameTitle)> discs, string mainGameTitle, string outputPath, int compressionLevel, IProgress<ProgressInfo> progress, Action<string, LogLevel, string> log, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var mainGameId = GameIdReader.ReadFromDisk(DiskSource.FromIso(discs[0].IsoPath));

            log($"[{mainGameId}] GameID 인식됨 ({discs.Count}개 디스크)", LogLevel.Info, mainGameId);

            var assets = new PbpAssets();
            var basePbpBytes = BaseResourceLoader.GetBasePbpBytes();

            PbpHeaderBuilder.EnsureRequiredAssets(assets, basePbpBytes);

            var sfo = BuildDefaultSfo(mainGameId, mainGameTitle);
            var header = PbpHeaderBuilder.BuildHeader(assets, sfo.Size);
            var psarOffset = header[9];
            using var outputStream = new FileStream(outputPath, FileMode.Create);

            WriteCommonSections(outputStream, header, sfo, assets, psarOffset);

            var isoStreams = new List<FileStream>();

            try
            {
                var discInfos = new List<DiscWriteInfo>();

                foreach (var (IsoPath, GameTitle) in discs)
                {
                    var isoSize = (uint)new FileInfo(IsoPath).Length;
                    var tocData = TocBuilder.BuildSingleTrackToc(isoSize);
                    var isoStream = new FileStream(IsoPath, FileMode.Open, FileAccess.Read);

                    isoStreams.Add(isoStream);
                    discInfos.Add(new DiscWriteInfo(isoStream, isoSize, mainGameId, GameTitle, tocData));
                }

                MultiDiscPsarWriter.WritePsar(outputStream, mainGameTitle, mainGameId, discInfos, psarOffset, compressionLevel, ct, (cur, total) => progress.Report(new ProgressInfo { Percent = (int)(cur * 100.0 / total) }));
            }
            finally
            {
                foreach (var s in isoStreams) 
                    s.Dispose();
            }

            StartDatWriter.WriteStartDat(outputStream, basePbpBytes, assets.BootPng);

            log($"완료: {Path.GetFileName(outputPath)}", LogLevel.Ok, mainGameId);

            return outputPath;
        }, ct);
    }

    private static SFOData BuildDefaultSfo(string gameId, string gameTitle)
    {
        var sfoBuilder = new SFOBuilder();

        sfoBuilder.AddEntry(SFOKeys.BOOTABLE, 0x01);
        sfoBuilder.AddEntry(SFOKeys.CATEGORY, SFOValues.PS1Category);
        sfoBuilder.AddEntry(SFOKeys.DISC_ID, gameId);
        sfoBuilder.AddEntry(SFOKeys.DISC_VERSION, "1.00");
        sfoBuilder.AddEntry(SFOKeys.LICENSE, SFOValues.License);
        sfoBuilder.AddEntry(SFOKeys.PARENTAL_LEVEL, SFOValues.ParentalLevel);
        sfoBuilder.AddEntry(SFOKeys.PSP_SYSTEM_VER, SFOValues.PSPSystemVersion);
        sfoBuilder.AddEntry(SFOKeys.REGION, 0x8000);
        sfoBuilder.AddEntry(SFOKeys.TITLE, gameTitle);

        return sfoBuilder.Build();
    }

    private static void WriteCommonSections(Stream outputStream, uint[] header, SFOData sfo, PbpAssets assets, uint psarOffset)
    {
        outputStream.Write(header, 0, 0x28);
        outputStream.WriteSFO(sfo);
        outputStream.WriteResource(assets.Icon0Png);
        outputStream.WriteResource(assets.Pic0Png);
        outputStream.WriteResource(assets.Pic1Png);
        outputStream.WriteResource(assets.DataPsp);

        var pos = (uint)outputStream.Position;

        for (var i = 0; i < psarOffset - pos; i++) 
            outputStream.WriteByte(0);
    }
}