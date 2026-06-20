namespace PBP.Core.Services;

/// <summary>
/// 원본: CueFileExtensions.GetDummyCueFile() + GetTOCData()를
/// "트랙 1개, MODE2/2352, 시작 0:0:0"인 단일 ISO/BIN 케이스로 특화한 버전.
/// 나중에 진짜 멀티트랙 CUE 파싱이 들어오면 이 옆에 일반화된 버전을 추가하면 됨.
/// </summary>
public static class TocBuilder
{
    public static byte[] BuildSingleTrackToc(uint isoSize)
    {
        var tocData = new byte[0xA * 4];
        var buf = new byte[0xA];

        var frames = isoSize / 2352;
        var (leadMin, leadSec, leadFrm) = PositionFromFrames(frames);

        var ctr = 0;

        buf[0] = 0x41; buf[1] = 0x00; buf[2] = 0xA0; buf[3] = 0x00; buf[4] = 0x00;
        buf[5] = 0x00; buf[6] = 0x00; buf[7] = ToBcd(1); buf[8] = ToBcd(0x20); buf[9] = 0x00;
        Array.Copy(buf, 0, tocData, ctr, 0xA); ctr += 0xA;

        buf[0] = 0x41; buf[2] = 0xA1; buf[7] = ToBcd(1); buf[8] = 0x00;
        Array.Copy(buf, 0, tocData, ctr, 0xA); ctr += 0xA;

        buf[0] = 0x01; buf[2] = 0xA2;
        buf[7] = ToBcd(leadMin); buf[8] = ToBcd(leadSec); buf[9] = ToBcd(leadFrm);
        Array.Copy(buf, 0, tocData, ctr, 0xA); ctr += 0xA;

        var (m2, s2, f2) = PositionFromFrames(150);
        buf[0] = 0x41; buf[1] = 0x00; buf[2] = ToBcd(1);
        buf[3] = ToBcd(0); buf[4] = ToBcd(0); buf[5] = ToBcd(0); buf[6] = 0x00;
        buf[7] = ToBcd(m2); buf[8] = ToBcd(s2); buf[9] = ToBcd(f2);
        Array.Copy(buf, 0, tocData, ctr, 0xA);

        return tocData;
    }

    private static byte ToBcd(int value) => (byte)((value / 10) * 0x10 + (value % 10));

    private static (int, int, int) PositionFromFrames(long frames)
    {
        var totalSeconds = (int)(frames / 75);

        return (totalSeconds / 60, totalSeconds % 60, (int)(frames % 75));
    }
}