// TitleTicket.cs
//
// Parses a Wii U title.tik (ticket) file and derives the real (decrypted) title key
// using the common key from WiiUKeyProvider.
//
// Ticket layout (offsets relative to start of a standard v0 ticket, signature type
// 0x10001 / RSA-2048 — the type used by virtually all released Wii U titles):
//   0x000            : signature type (uint32, big-endian)
//   0x004            : signature (0x100 bytes) + padding -> header total 0x140 bytes
//   0x1BF            : encrypted title key (16 bytes)
//   0x1DC            : title ID (8 bytes, big-endian) — also used as the AES-CBC IV
//                       (IV = titleId || 8 zero bytes)
//
// Source: publicly documented ticket format (WiiBrew "Ticket" page and Wii U CDN/NUS
// tooling that follows the same layout). No secret key material is embedded here —
// the common key must come from WiiUKeyProvider, supplied by the user.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace WiiU.Core.Services;

public sealed class TitleTicket
{
    private const uint ExpectedSignatureType = 0x10001; // RSA-2048, the layout this class assumes
    private const int EncryptedTitleKeyOffset = 0x1BF;
    private const int TitleIdOffset = 0x1DC;

    public ulong TitleId { get; }
    public byte[] TitleIdBytes { get; }
    public byte[] EncryptedTitleKey { get; }

    private TitleTicket(ulong titleId, byte[] titleIdBytes, byte[] encryptedTitleKey)
    {
        TitleId = titleId;
        TitleIdBytes = titleIdBytes;
        EncryptedTitleKey = encryptedTitleKey;
    }

    public static TitleTicket ParseFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Parse(fs);
    }

    public static TitleTicket Parse(Stream tikStream)
    {
        Span<byte> sigTypeBuf = stackalloc byte[4];
        tikStream.ReadExactly(sigTypeBuf);
        uint sigType = BinaryPrimitives.ReadUInt32BigEndian(sigTypeBuf);
        if (sigType != ExpectedSignatureType)
        {
            throw new NotSupportedException(
                $"Unexpected ticket signature type 0x{sigType:X}. This ticket uses a different " +
                "signature scheme than the standard RSA-2048 layout this parser assumes, so the " +
                "fixed offsets (0x1BF/0x1DC) may not apply. Needs a variable-header-size implementation.");
        }

        Span<byte> titleKeyBuf = stackalloc byte[16];
        tikStream.Position = EncryptedTitleKeyOffset;
        tikStream.ReadExactly(titleKeyBuf);

        Span<byte> titleIdBuf = stackalloc byte[8];
        tikStream.Position = TitleIdOffset;
        tikStream.ReadExactly(titleIdBuf);

        ulong titleId = BinaryPrimitives.ReadUInt64BigEndian(titleIdBuf);
        return new TitleTicket(titleId, titleIdBuf.ToArray(), titleKeyBuf.ToArray());
    }

    /// <summary>Decrypts and returns the real 16-byte title key using the Wii U common key.</summary>
    public byte[] DecryptTitleKey(WiiUKeyProvider keys)
    {
        Span<byte> iv = stackalloc byte[16];
        TitleIdBytes.CopyTo(iv); // first 8 bytes = title ID, remaining 8 bytes stay zero

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = keys.CommonKey.ToArray();
        aes.IV = iv.ToArray();

        using var decryptor = aes.CreateDecryptor();
        var titleKey = new byte[16];
        decryptor.TransformBlock(EncryptedTitleKey, 0, 16, titleKey, 0);
        return titleKey;
    }

    /// <summary>16-digit lowercase hex title ID, e.g. "0005000e10102000" — matches the .wua
    /// subfolder naming convention (titleId + "_v" + version).</summary>
    public string TitleIdHex => TitleId.ToString("x16");
}