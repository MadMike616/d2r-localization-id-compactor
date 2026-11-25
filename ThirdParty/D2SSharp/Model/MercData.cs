using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Mercenary save data in the header (32 bytes).
/// </summary>
public partial class MercData
{
    /// <summary>Mercenary flags (only bit 16 = dead is used, all other bits are always zero).</summary>
    public uint Flags { get; set; }

    /// <summary>Mercenary random seed.</summary>
    public uint Seed { get; set; }

    /// <summary>Name index relative to hireling type.</summary>
    public ushort NameIndex { get; set; }

    /// <summary>Hireling type ID.</summary>
    public ushort HirelingId { get; set; }

    /// <summary>Mercenary experience points.</summary>
    public uint Experience { get; set; }

    /// <summary>Whether the mercenary is dead (bit 16).</summary>
    public bool IsDead
    {
        get => (Flags & 0x00010000) != 0;
        set => Flags = value ? Flags | 0x00010000u : Flags & ~0x00010000u;
    }

    /// <summary>Whether a mercenary exists (has been hired).</summary>
    public bool HasMerc => Seed != 0 || NameIndex != 0 || Experience != 0;

    /// <summary>
    /// Reads mercenary data from a BitReader.
    /// </summary>
    public static MercData Read(ref BitReader reader)
    {
        return new MercData
        {
            Flags = reader.ReadUInt32(),
            Seed = reader.ReadUInt32(),
            NameIndex = reader.ReadUInt16(),
            HirelingId = reader.ReadUInt16(),
            Experience = reader.ReadUInt32()
        };
    }

    /// <summary>
    /// Writes mercenary data to a BitWriter.
    /// </summary>
    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt32(Flags);
        writer.WriteUInt32(Seed);
        writer.WriteUInt16(NameIndex);
        writer.WriteUInt16(HirelingId);
        writer.WriteUInt32(Experience);
    }
}
