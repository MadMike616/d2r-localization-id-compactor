using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Chronicle section found in chronicle stash tabs (StashTabType.Chronicle).
/// Tracks which set, unique, and runeword items have been found.
/// </summary>
public partial class ChronicleSection
{
    /// <summary>Section magic: 0xC0EAEDC0</summary>
    public const uint Magic = 0xC0EAEDC0;

    /// <summary>Chronicle format version (expected 1).</summary>
    public ushort Version { get; set; } = 1;

    /// <summary>Set item chronicle entries.</summary>
    public List<ChronicleEntry> SetEntries { get; set; } = [];

    /// <summary>Unique item chronicle entries.</summary>
    public List<ChronicleEntry> UniqueEntries { get; set; } = [];

    /// <summary>Runeword chronicle entries.</summary>
    public List<ChronicleEntry> RunewordEntries { get; set; } = [];

    /// <summary>Reserved bytes after the counts (8 bytes, always written as zero and never read by the game).</summary>
    public byte[] Reserved { get; set; } = new byte[8];

    /// <summary>
    /// Trailing data after the chronicle entries.
    /// The game writer includes 64 bytes of stale buffer data due to a size
    /// calculation bug (add ax, 40h at 0x140311e98). The reader ignores these
    /// bytes, but we preserve them for byte-exact round-trip.
    /// </summary>
    public byte[] TrailingData { get; set; } = [];

    /// <summary>
    /// Reads a chronicle section from the tab body.
    /// </summary>
    /// <param name="reader">The bit reader positioned at the start of the tab body (after 64-byte header).</param>
    /// <param name="bodySize">Total body size in bytes (tab Size - 64).</param>
    public static ChronicleSection Read(ref BitReader reader, int bodySize)
    {
        int bodyStart = reader.BytePosition;
        var section = new ChronicleSection();

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid chronicle magic: 0x{magic:X8}, expected 0x{Magic:X8}");

        section.Version = reader.ReadUInt16();
        ushort setCount = reader.ReadUInt16();
        ushort uniqueCount = reader.ReadUInt16();
        ushort runewordCount = reader.ReadUInt16();
        reader.ReadBytes(section.Reserved);

        // Read entries (10 bytes each: uint32 ID + uint16 Source + uint32 Timestamp)
        // All fields are byte-aligned, so we use regular reads.
        for (int i = 0; i < setCount; i++)
            section.SetEntries.Add(ReadEntry(ref reader));

        for (int i = 0; i < uniqueCount; i++)
            section.UniqueEntries.Add(ReadEntry(ref reader));

        for (int i = 0; i < runewordCount; i++)
            section.RunewordEntries.Add(ReadEntry(ref reader));

        // Capture trailing data (stale buffer bytes included by the writer's size bug)
        int consumed = reader.BytePosition - bodyStart;
        int trailing = bodySize - consumed;
        if (trailing > 0)
        {
            section.TrailingData = new byte[trailing];
            reader.ReadBytes(section.TrailingData);
        }

        return section;
    }

    /// <summary>
    /// Writes a chronicle section to the tab body.
    /// </summary>
    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt32(Magic);
        writer.WriteUInt16(Version);
        writer.WriteUInt16((ushort)SetEntries.Count);
        writer.WriteUInt16((ushort)UniqueEntries.Count);
        writer.WriteUInt16((ushort)RunewordEntries.Count);
        writer.WriteBytes(Reserved);

        foreach (var entry in SetEntries)
            WriteEntry(ref writer, entry);

        foreach (var entry in UniqueEntries)
            WriteEntry(ref writer, entry);

        foreach (var entry in RunewordEntries)
            WriteEntry(ref writer, entry);

        // Write trailing data for round-trip fidelity
        if (TrailingData.Length > 0)
            writer.WriteBytes(TrailingData);
    }

    private static ChronicleEntry ReadEntry(ref BitReader reader)
    {
        uint itemId = reader.ReadUInt32();
        ushort source = reader.ReadUInt16();
        uint timestamp = reader.ReadUInt32();
        return new ChronicleEntry { ItemId = itemId, Source = source, Timestamp = timestamp };
    }

    private static void WriteEntry(ref BitWriter writer, ChronicleEntry entry)
    {
        writer.WriteUInt32(entry.ItemId);
        writer.WriteUInt16(entry.Source);
        writer.WriteUInt32(entry.Timestamp);
    }
}

/// <summary>
/// A single chronicle entry tracking a found item.
/// </summary>
public record struct ChronicleEntry
{
    /// <summary>items.txt row index for set/unique items; runeword string index for runewords.</summary>
    public uint ItemId { get; set; }

    /// <summary>
    /// String table ID of the name of whatever dropped the item (monster, superunique or object).
    /// 0 for runewords. The game writes 0x6A5D for set/unique entries with an unknown spawner.
    /// </summary>
    public ushort Source { get; set; }

    /// <summary>When the item was found, in minutes since the Unix epoch (UTC).</summary>
    public uint Timestamp { get; set; }

    /// <summary><see cref="Timestamp"/> as a UTC <see cref="DateTime"/>.</summary>
    public readonly DateTime TimestampUtc => DateTime.UnixEpoch.AddMinutes(Timestamp);
}
