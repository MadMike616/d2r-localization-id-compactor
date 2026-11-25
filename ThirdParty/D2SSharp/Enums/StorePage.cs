namespace D2SSharp.Enums;

/// <summary>
/// Storage page for items in inventory.
/// Values match C++ D2C_ItemInvPage enum (internal representation).
/// Note: File format stores nInvPage + 1, so read/write must adjust.
/// </summary>
public enum StorePage : byte
{
    /// <summary>Main inventory.</summary>
    Inventory = 0,

    /// <summary>Equipment page (internal use).</summary>
    Equip = 1,

    /// <summary>Trade page (internal use).</summary>
    Trade = 2,

    /// <summary>Horadric Cube.</summary>
    Cube = 3,

    /// <summary>Stash.</summary>
    Stash = 4,

    /// <summary>Belt (internal use).</summary>
    Belt = 5,

    /// <summary>Null/invalid page.</summary>
    Null = 255
}
