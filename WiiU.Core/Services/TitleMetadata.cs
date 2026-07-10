// TitleMetadata.cs
//
// Parses a Wii U title.tmd file into its content record list.
// Layout (standard RSA-2048-signed TMD, the type used by virtually all released titles):
//
//   0x000 : sig_type          uint32 BE  (expected 0x00010001 = RSA_2048)
//   0x004 : signature          256 bytes
//   0x104 : fill1              60 bytes
//   0x140 : issuer             64 bytes
//   0x180 : version            u8
//   0x181 : ca_crl_version     u8
//   0x182 : signer_crl_version u8
//   0x183 : vwii/fill          u8
//   0x184 : sys_version        u64 BE
//   0x18C : title_id           u64 BE
//   0x194 : title_type         u32 BE
//   0x198 : group_id           u16 BE
//   0x19A : reserved           62 bytes
//   0x1D8 : access_rights      u32 BE
//   0x1DC : title_version      u16 BE
//   0x1DE : num_contents       u16 BE
//   0x1E0 : boot_index         u16 BE
//   0x1E2 : fill2              u16 BE
//   0x1E4 : content_record[num_contents]   -- each record is 0x24 (36) bytes:
//              u32 content_id
//              u16 index          // matches the .app/.h3 filename, e.g. 00000000.app
//              u16 type           // bitmask; bit 1 (0x2) set => a matching .h3 hash file exists
//              u64 size
//              u8  hash[20]       // SHA-1 of the *decrypted* content
//
// Source: publicly documented TMD format (WiiBrew "Title metadata" page, applies
// identically to Wii U titles per WiiUBrew's mirror of the same page).

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace WiiU.Core.Services;

public sealed record ContentRecord(uint ContentId, ushort Index, ushort Type, ulong Size, byte[] Hash)
{
    /// <summary>True if a matching &lt;index&gt;.h3 hash-tree file should exist alongside the content.</summary>
    public bool HasH3Hash => (Type & 0x2) != 0;

    /// <summary>Filename as stored on disc/NUS, e.g. "00000000.app".</summary>
    public string AppFileName => $"{Index:x8}.app";
    public string H3FileName => $"{Index:x8}.h3";
}

public sealed class TitleMetadata
{
    private const int ContentRecordsOffset = 0x1E4;
    private const int ContentRecordSize = 0x24;
    private const uint ExpectedSignatureType = 0x10001; // RSA-2048 — see TitleTicket.cs for the same assumption

    public ulong TitleId { get; }
    public ushort TitleVersion { get; }
    public ushort BootIndex { get; }
    public IReadOnlyList<ContentRecord> Contents { get; }

    private TitleMetadata(ulong titleId, ushort titleVersion, ushort bootIndex, List<ContentRecord> contents)
    {
        TitleId = titleId;
        TitleVersion = titleVersion;
        BootIndex = bootIndex;
        Contents = contents;
    }

    public static TitleMetadata ParseFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Parse(fs);
    }

    public static TitleMetadata Parse(Stream tmdStream)
    {
        Span<byte> sigTypeBuf = stackalloc byte[4];
        tmdStream.ReadExactly(sigTypeBuf);
        uint sigType = BinaryPrimitives.ReadUInt32BigEndian(sigTypeBuf);
        if (sigType != ExpectedSignatureType)
        {
            throw new NotSupportedException(
                $"Unexpected TMD signature type 0x{sigType:X}. This parser assumes the standard " +
                "RSA-2048 header layout (fixed offsets up to 0x1E4); a different signature type " +
                "shifts every offset after the signature block.");
        }

        Span<byte> headerTail = stackalloc byte[16];

        tmdStream.Position = 0x18C;
        tmdStream.ReadExactly(headerTail[..8]);
        ulong titleId = BinaryPrimitives.ReadUInt64BigEndian(headerTail[..8]);

        tmdStream.Position = 0x1DC;
        tmdStream.ReadExactly(headerTail[..6]);
        ushort titleVersion = BinaryPrimitives.ReadUInt16BigEndian(headerTail[0..2]);
        ushort numContents = BinaryPrimitives.ReadUInt16BigEndian(headerTail[2..4]);
        ushort bootIndex = BinaryPrimitives.ReadUInt16BigEndian(headerTail[4..6]);

        var contents = new List<ContentRecord>(numContents);
        tmdStream.Position = ContentRecordsOffset;
        Span<byte> rec = stackalloc byte[ContentRecordSize];
        for (int i = 0; i < numContents; i++)
        {
            tmdStream.ReadExactly(rec);
            uint contentId = BinaryPrimitives.ReadUInt32BigEndian(rec[0..4]);
            ushort index = BinaryPrimitives.ReadUInt16BigEndian(rec[4..6]);
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(rec[6..8]);
            ulong size = BinaryPrimitives.ReadUInt64BigEndian(rec[8..16]);
            var hash = rec[16..36].ToArray();
            contents.Add(new ContentRecord(contentId, index, type, size, hash));
        }

        return new TitleMetadata(titleId, titleVersion, bootIndex, contents);
    }

    /// <summary>
    /// Builds the 16-byte AES-CBC IV used to decrypt a given content: the content's
    /// index as a big-endian uint16, followed by 14 zero bytes.
    /// </summary>
    public static byte[] BuildContentIv(ushort contentIndex)
    {
        var iv = new byte[16];
        BinaryPrimitives.WriteUInt16BigEndian(iv.AsSpan(0, 2), contentIndex);
        return iv;
    }
}