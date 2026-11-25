using D2SSharp.Data;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Items section container - extends <see cref="List{T}"/> for direct item access.
/// </summary>
public partial class ItemsSection : List<Item>
{
    /// <summary>Section magic: "JM" (0x4D4A)</summary>
    public const ushort Magic = 0x4D4A;

    /// <summary>
    /// Reads an items section from a BitReader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public static ItemsSection Read(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        var section = new ItemsSection();

        // Verify magic "JM"
        ushort magic = reader.ReadUInt16();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid items section magic: 0x{magic:X4}");

        ushort itemCount = reader.ReadUInt16();

        for (int i = 0; i < itemCount; i++)
        {
            // Item.Read handles socketed items internally
            var item = Item.Read(ref reader, externalData, saveVersion);
            section.Add(item);
        }

        return section;
    }

    /// <summary>
    /// Calculates an estimated buffer size for this items section.
    /// Uses 200 bytes per item (including socketed items) as a conservative estimate.
    /// </summary>
    public int EstimateSize()
    {
        return 4 + CountRecursive(this) * 200;

        static int CountRecursive(IEnumerable<Item?> items)
        {
            int count = 0;
            foreach (var item in items)
            {
                if (item == null) continue;
                count++;
                count += CountRecursive(item.Sockets);
            }
            return count;
        }
    }

    /// <summary>
    /// Writes an items section to a BitWriter.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public void Write(ref BitWriter writer, IExternalData externalData, uint saveVersion)
    {
        // Write magic "JM"
        writer.WriteUInt16(Magic);

        writer.WriteUInt16((ushort)Count);

        foreach (var item in this)
        {
            // Item.Write handles socketed items internally
            item.Write(ref writer, externalData, saveVersion);
        }
    }
}
