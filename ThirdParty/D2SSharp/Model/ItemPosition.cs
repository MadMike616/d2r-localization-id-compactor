using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Item position/location data.
/// </summary>
public partial class ItemPosition
{
    /// <summary>Animation/storage mode.</summary>
    public ItemMode Mode { get; set; }

    /// <summary>World X position (when on ground).</summary>
    public ushort GroundX { get; set; }

    /// <summary>World Y position (when on ground).</summary>
    public ushort GroundY { get; set; }

    /// <summary>Body location (when equipped).</summary>
    public BodyLocation BodyLocation { get; set; }

    /// <summary>Inventory X position (0-15).</summary>
    public byte InvX { get; set; }

    /// <summary>Inventory Y position (0-15).</summary>
    public byte InvY { get; set; }

    /// <summary>Storage page (inventory, cube, stash).</summary>
    public StorePage StorePage { get; set; }

    /// <summary>
    /// Whether this item is on the ground.
    /// </summary>
    public bool IsOnGround => Mode == ItemMode.OnGround || Mode == ItemMode.Dropping;

    /// <summary>
    /// Reads an item position from a BitReader.
    /// </summary>
    public static ItemPosition Read(ref BitReader reader, ItemMode mode)
    {
        var pos = new ItemPosition { Mode = mode };

        if (mode == ItemMode.OnGround || mode == ItemMode.Dropping)
        {
            pos.GroundX = (ushort)reader.ReadBits(16);
            pos.GroundY = (ushort)reader.ReadBits(16);
        }
        else
        {
            pos.BodyLocation = (BodyLocation)reader.ReadBits(4);
            pos.InvX = (byte)reader.ReadBits(4);
            pos.InvY = (byte)reader.ReadBits(4);
            // C++: nInvPage = raw - 1 (file stores nInvPage + 1)
            pos.StorePage = (StorePage)(reader.ReadBits(3) - 1);
        }

        return pos;
    }

    /// <summary>
    /// Writes an item position to a BitWriter.
    /// </summary>
    public void Write(ref BitWriter writer)
    {
        if (Mode == ItemMode.OnGround || Mode == ItemMode.Dropping)
        {
            writer.WriteBits(GroundX, 16);
            writer.WriteBits(GroundY, 16);
        }
        else
        {
            writer.WriteBits((uint)BodyLocation, 4);
            // C++: clamps X/Y to 0-15 (4 bits)
            writer.WriteBits(Math.Min(InvX, (byte)15), 4);
            writer.WriteBits(Math.Min(InvY, (byte)15), 4);
            // C++: writes nInvPage + 1 (file stores nInvPage + 1)
            // Raw 0 = null/not applicable (e.g., socketed items), raw 1-6 = pages 0-5
            uint rawStorePage = StorePage == StorePage.Null ? 0u : Math.Min((uint)StorePage + 1, 7u);
            writer.WriteBits(rawStorePage, 3);
        }
    }
}
