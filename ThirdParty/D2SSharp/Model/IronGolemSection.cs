using D2SSharp.Data;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Iron golem section (expansion only, Necromancer only).
/// Contains the item used to create an Iron Golem.
/// </summary>
public partial class IronGolemSection
{
    /// <summary>Section magic: "kf" (0x666B)</summary>
    public const ushort Magic = 0x666B;

    /// <summary>The item the iron golem was created from (null if no golem).</summary>
    public Item? GolemItem { get; set; }

    /// <summary>
    /// Reads an iron golem section from a BitReader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public static IronGolemSection Read(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        var section = new IronGolemSection();

        // Verify magic "kf"
        ushort magic = reader.ReadUInt16();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid iron golem section magic: 0x{magic:X4}");

        byte hasGolem = reader.ReadByte();

        if (hasGolem != 0)
        {
            section.GolemItem = Item.Read(ref reader, externalData, saveVersion);
        }

        return section;
    }

    /// <summary>
    /// Writes an iron golem section to a BitWriter.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public void Write(ref BitWriter writer, IExternalData externalData, uint saveVersion)
    {
        // Write magic "kf"
        writer.WriteUInt16(Magic);

        writer.WriteByte((byte)(GolemItem != null ? 1 : 0));

        GolemItem?.Write(ref writer, externalData, saveVersion);
    }
}
