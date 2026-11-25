namespace D2SSharp.Enums;

/// <summary>
/// Item animation/storage mode.
/// Values match C++ D2C_ItemModes enum.
/// </summary>
public enum ItemMode : byte
{
    /// <summary>Item is stored in inventory/stash/cube.</summary>
    Stored = 0,

    /// <summary>Item is equipped on character.</summary>
    Equipped = 1,

    /// <summary>Item is in belt.</summary>
    InBelt = 2,

    /// <summary>Item is on the ground.</summary>
    OnGround = 3,

    /// <summary>Item is on cursor.</summary>
    OnCursor = 4,

    /// <summary>Item is dropping/being thrown.</summary>
    Dropping = 5,

    /// <summary>Item is socketed in another item.</summary>
    Socketed = 6
}
