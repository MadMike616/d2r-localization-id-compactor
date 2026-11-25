using System.Text;
using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Represents a compact preview of an equipped item for character selection display.
/// Contains minimal item data needed to render the character in the selection screen.
/// </summary>
public partial class PreviewItem
{
    /// <summary>4-character item type code stored as a 32-bit integer (e.g., "hax " for hand axe).</summary>
    public uint Code { get; set; }

    /// <summary>Color transform index applied to the item graphic.</summary>
    public byte Transform { get; set; }

    /// <summary>Quality level of the item (normal, magic, rare, unique, etc.).</summary>
    public ItemQuality Quality { get; set; }

    /// <summary>Index into the item graphics file for this item type.</summary>
    public ushort FileIndex { get; set; }

    /// <summary>Item flags indicating various item properties.</summary>
    public ItemFlags Flags { get; set; }

    /// <summary>Gets the item code as a readable ASCII string with padding removed.</summary>
    public string CodeString => Encoding.ASCII.GetString(BitConverter.GetBytes(Code)).TrimEnd(['\0', ' ']);

    public static PreviewItem Read(ref BitReader reader)
    {
        var previewItem = new PreviewItem
        {
            Code = reader.ReadUInt32(),
            Transform = reader.ReadByte(),
            Quality = (ItemQuality)reader.ReadByte(),
            FileIndex = reader.ReadUInt16(),
            Flags = (ItemFlags)reader.ReadUInt32()
        };

        return previewItem;
    }

    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt32(Code);
        writer.WriteByte(Transform);
        writer.WriteByte((byte)Quality);
        writer.WriteUInt16(FileIndex);
        writer.WriteUInt32((uint)Flags);
    }
}
