namespace D2SSharp.Enums;

/// <summary>
/// Item state flags stored in the first 32 bits of an item record.
/// Controls item behavior, appearance, and serialization format.
/// </summary>
[Flags]
public enum ItemFlags : uint
{
    NewItem = 0x00000001,
    Target = 0x00000002,
    Targeting = 0x00000004,
    Deleted = 0x00000008,
    Identified = 0x00000010,
    HasQuantity = 0x00000020,
    SwitchIn = 0x00000040,
    SwitchOut = 0x00000080,
    Broken = 0x00000100,
    Repaired = 0x00000200,
    Socketed = 0x00000800,
    NoSell = 0x00001000,
    InStore = 0x00002000,
    NoEquip = 0x00004000,
    Named = 0x00008000,
    IsEar = 0x00010000,
    StarterItem = 0x00020000,
    Init = 0x00080000,
    CompactSave = 0x00200000,
    Ethereal = 0x00400000,
    JustSaved = 0x00800000,
    Personalized = 0x01000000,
    LowQuality = 0x02000000,
    Runeword = 0x04000000,
    Duplicated = 0x08000000,
    Chronicle = 0x10000000,
    ChronicleCompact = 0x20000000
}
