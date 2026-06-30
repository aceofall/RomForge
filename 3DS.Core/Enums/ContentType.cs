namespace _3DS.Core.Enums;

[Flags]
public enum ContentType : ushort
{
    Encrypted = 0x0001,
    Disc = 0x0002,
    CFM = 0x0004,
    Optional = 0x4000,
    Shared = 0x8000,
}