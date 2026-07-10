// WiiUKeyProvider.cs
//
// Reads Wii U decryption keys from an external file supplied by the user.
// RomForge itself ships with no key material — the common key (and optionally
// per-title key overrides) must be provided out-of-band, e.g. extracted by the
// user from their own console.
//
// Supported file format ("keys.txt"-style, one entry per line, '#' for comments):
//
//   commonKey = 32-hex-chars-here
//   # optional per-title overrides (rarely needed — normally derived from title.tik):
//   0005000e10102000 = 32-hex-chars-title-key
//
// Whitespace around '=' and blank lines are ignored. Hex is case-insensitive.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WiiU.Core.Services;

public sealed class WiiUKeyProvider
{
    private byte[]? _commonKey;
    private readonly Dictionary<string, byte[]> _titleKeyOverrides = new(StringComparer.OrdinalIgnoreCase);

    private WiiUKeyProvider() { }

    public static WiiUKeyProvider LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Wii U key file not found: {path}", path);

        var provider = new WiiUKeyProvider();
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
                throw new InvalidDataException($"Malformed key file line (expected 'key = value'): \"{rawLine}\"");

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            var bytes = ParseHex(value, rawLine);

            if (string.Equals(key, "commonKey", StringComparison.OrdinalIgnoreCase))
            {
                if (bytes.Length != 16)
                    throw new InvalidDataException("commonKey must be exactly 16 bytes (32 hex chars).");
                provider._commonKey = bytes;
            }
            else
            {
                // treat as a titleId -> titleKey override
                var normalizedTitleId = key.Replace("0x", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
                if (bytes.Length != 16)
                    throw new InvalidDataException($"Title key override for {key} must be exactly 16 bytes (32 hex chars).");
                provider._titleKeyOverrides[normalizedTitleId] = bytes;
            }
        }

        if (provider._commonKey is null)
            throw new InvalidDataException("Key file did not contain a 'commonKey' entry.");

        return provider;
    }

    /// <summary>The Wii U common key (16 bytes), used to decrypt the title key stored in title.tik.</summary>
    public ReadOnlySpan<byte> CommonKey => _commonKey!;

    /// <summary>
    /// Returns a manually-provided title key override for the given 16-hex-digit title ID, if present.
    /// Normally not needed — the title key should be derived from title.tik + CommonKey instead.
    /// </summary>
    public bool TryGetTitleKeyOverride(string titleIdHex, out byte[] titleKey)
    {
        var normalized = titleIdHex.Replace("0x", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return _titleKeyOverrides.TryGetValue(normalized, out titleKey!);
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf('#');
        return idx < 0 ? line : line[..idx];
    }

    private static byte[] ParseHex(string hex, string context)
    {
        hex = hex.Replace(" ", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length % 2 != 0)
            throw new InvalidDataException($"Odd-length hex value in key file: \"{context}\"");

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                throw new InvalidDataException($"Invalid hex value in key file: \"{context}\"");
        }
        return bytes;
    }
}