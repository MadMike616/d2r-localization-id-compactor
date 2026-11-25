using D2SSharp.Data;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Corpse section (contains items on dead player's corpses).
/// C++ supports multiple corpses (D2_NUM_CORPSES), though typically only 0 or 1 exist.
/// </summary>
public partial class CorpseSection : List<Corpse>
{
    /// <summary>Section magic: "JM" (0x4D4A)</summary>
    public const ushort Magic = 0x4D4A;

    /// <summary>
    /// Reads a corpse section from a BitReader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public static CorpseSection Read(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        var section = new CorpseSection();

        // Verify magic "JM"
        ushort magic = reader.ReadUInt16();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid corpse section magic: 0x{magic:X4}");

        ushort corpseCount = reader.ReadUInt16();

        for (int i = 0; i < corpseCount; i++)
        {
            section.Add(Corpse.Read(ref reader, externalData, saveVersion));
        }

        return section;
    }

    /// <summary>
    /// Writes a corpse section to a BitWriter.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public void Write(ref BitWriter writer, IExternalData externalData, uint saveVersion)
    {
        // Write magic "JM"
        writer.WriteUInt16(Magic);

        writer.WriteUInt16((ushort)Count);

        foreach (var corpse in this)
        {
            corpse.Write(ref writer, externalData, saveVersion);
        }
    }
}

/// <summary>
/// A player corpse with position and items.
/// </summary>
public partial class Corpse
{
    /// <summary>Unused (the game never initializes it, so it holds stack garbage; ignored on load).</summary>
    public uint Unused { get; set; }

    /// <summary>Corpse X position.</summary>
    public uint X { get; set; }

    /// <summary>Corpse Y position.</summary>
    public uint Y { get; set; }

    /// <summary>Items on the corpse.</summary>
    public ItemsSection Items { get; set; } = new();

    /// <summary>
    /// Reads a corpse from a BitReader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public static Corpse Read(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        var corpse = new Corpse
        {
            Unused = reader.ReadUInt32(),
            X = reader.ReadUInt32(),
            Y = reader.ReadUInt32(),
            Items = ItemsSection.Read(ref reader, externalData, saveVersion)
        };

        return corpse;
    }

    /// <summary>
    /// Writes a corpse to a BitWriter.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public void Write(ref BitWriter writer, IExternalData externalData, uint saveVersion)
    {
        writer.WriteUInt32(Unused);
        writer.WriteUInt32(X);
        writer.WriteUInt32(Y);
        Items.Write(ref writer, externalData, saveVersion);
    }
}
