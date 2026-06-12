namespace Patch.Core.Services;

/// <summary>
/// 기존 PatchCore.Formats (Ips, Ups, Bps, Xdelta3 등) 를 byte[] 인터페이스로 래핑.
/// 실제 구현 시 각 포맷 Apply 메서드 연결.
/// </summary>
public static class UniversalPatcher
{
    public static byte[] Apply(byte[] source, byte[] patch, Action<double>? progress = null)
    {
        string fmt = DetectFormat(patch);

        return fmt switch
        {
            "IPS" => ApplyIps(source, patch, progress),
            "UPS" => ApplyUps(source, patch, progress),
            "BPS" => ApplyBps(source, patch, progress),
            "XDELTA" => ApplyXdelta(source, patch, progress),
            "PPF" => ApplyPpf(source, patch, progress),
            "APS" => ApplyAps(source, patch, progress),
            _ => throw new NotSupportedException($"지원하지 않는 패치 포맷")
        };
    }

    private static string DetectFormat(byte[] patch)
    {
        if (patch.Length < 4) return "UNKNOWN";

        // IPS: "PATCH"
        if (patch.Length >= 5
            && patch[0] == (byte)'P' && patch[1] == (byte)'A'
            && patch[2] == (byte)'T' && patch[3] == (byte)'C' && patch[4] == (byte)'H')
            return "IPS";
        // UPS: "UPS1"
        if (patch[0] == (byte)'U' && patch[1] == (byte)'P'
            && patch[2] == (byte)'S' && patch[3] == (byte)'1')
            return "UPS";
        // BPS: "BPS1"
        if (patch[0] == (byte)'B' && patch[1] == (byte)'P'
            && patch[2] == (byte)'S' && patch[3] == (byte)'1')
            return "BPS";
        // xdelta3: 마법 바이트 0xD6 0xC3 0xC4
        if (patch[0] == 0xD6 && patch[1] == 0xC3 && patch[2] == 0xC4)
            return "XDELTA";
        // PPF: "PPF"
        if (patch[0] == (byte)'P' && patch[1] == (byte)'P' && patch[2] == (byte)'F')
            return "PPF";

        return "UNKNOWN";
    }

    // TODO: 기존 PatchCore.Formats 클래스들 연결
    private static byte[] ApplyIps(byte[] src, byte[] patch, Action<double>? progress)
        => throw new NotImplementedException("IPS TODO");
    private static byte[] ApplyUps(byte[] src, byte[] patch, Action<double>? progress)
        => throw new NotImplementedException("UPS TODO");
    private static byte[] ApplyBps(byte[] src, byte[] patch, Action<double>? progress)
        => throw new NotImplementedException("BPS TODO");
    private static byte[] ApplyXdelta(byte[] src, byte[] patch, Action<double>? progress)
        => throw new NotImplementedException("Xdelta TODO");
    private static byte[] ApplyPpf(byte[] src, byte[] patch, Action<double>? progress)
        => throw new NotImplementedException("PPF TODO");
    private static byte[] ApplyAps(byte[] src, byte[] patch, Action<double>? progress)
        => throw new NotImplementedException("APS TODO");
}