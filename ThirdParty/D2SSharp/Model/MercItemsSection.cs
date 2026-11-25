using D2SSharp.Data;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Mercenary items section (expansion only).
/// </summary>
public partial class MercItemsSection
{
    /// <summary>Section magic: "jf" (0x666A)</summary>
    public const ushort Magic = 0x666A;

    /// <summary>Mercenary equipped items (empty if no mercenary).</summary>
    public ItemsSection Items { get; set; } = new();

    /// <summary>
    /// Reads a merc items section from a BitReader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="hasMerc">Whether a mercenary exists (from MercData.HasMerc).</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public static MercItemsSection Read(ref BitReader reader, IExternalData externalData, bool hasMerc, uint saveVersion)
    {
        var section = new MercItemsSection();

        // Verify magic "jf"
        ushort magic = reader.ReadUInt16();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid merc items section magic: 0x{magic:X4}");

        // C++ only writes the 'JM' items section if a mercenary exists.
        // If no merc, the next section ('kf' iron golem) follows immediately.
        if (hasMerc)
        {
            section.Items = ItemsSection.Read(ref reader, externalData, saveVersion);
        }

        return section;
    }

    /// <summary>
    /// Writes a merc items section to a BitWriter.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="hasMerc">Whether a mercenary exists (from MercData.HasMerc).</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public void Write(ref BitWriter writer, IExternalData externalData, bool hasMerc, uint saveVersion)
    {
        // Write magic "jf"
        writer.WriteUInt16(Magic);

        // C++ writes 'JM' section if merc exists (even with 0 items)
        if (hasMerc)
            Items.Write(ref writer, externalData, saveVersion);
    }
}
