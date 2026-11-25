using D2SSharp.Data;
using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// A single Diablo 2 Resurrected shared stash tab.
/// </summary>
public partial class D2StashTab
{
    /// <summary>Size of the stash tab header</summary>
    public const uint HeaderSize = 64;

    /// <summary>Tab magic value: 0xAA55AA55</summary>
    public const uint Magic = 0xAA55AA55;

    /// <summary>Stash format.</summary>
    public uint StashFormat { get; set; }

    /// <summary>Save version used to serialize the items in this tab (same as the .d2s save version).</summary>
    public uint SaveVersion { get; set; }

    /// <summary>Gold amount stored in the stash tab.</summary>
    public uint Gold { get; set; }

    /// <summary>Season number the stash belongs to (create_season stat, 0 for non-seasonal).</summary>
    public ushort Season { get; set; }

    /// <summary>Padding after StashTabType (3 bytes, always zero).</summary>
    public byte[] Padding21 { get; set; } = new byte[3];

    /// <summary>
    /// Opaque withdraw-only tab data (20 bytes at header offset 24).
    /// When the first uint32 is non-zero the game only allows taking items out of the tab,
    /// and deletes the tab on load once it holds no gold and no items.
    /// The game never creates this data itself, it only preserves it on save.
    /// </summary>
    public byte[] WithdrawOnlyData { get; set; } = new byte[20];

    /// <summary>Padding at the end of the header (20 bytes, always zero).</summary>
    public byte[] Padding44 { get; set; } = new byte[20];

    /// <summary>Whether the tab is withdraw-only (items can be taken out but not put in).</summary>
    public bool IsWithdrawOnly => BitConverter.ToUInt32(WithdrawOnlyData, 0) != 0;

    /// <summary>
    /// Stash tab type (StashFormat >= 2 only).
    /// Determines what the tab body contains.
    /// </summary>
    public StashTabType TabType { get; set; }

    /// <summary>Items stored in the stash tab (for Normal and AdvancedStash tabs).</summary>
    public ItemsSection Items { get; set; } = new();

    /// <summary>Chronicle data (for Chronicle tabs only).</summary>
    public ChronicleSection? Chronicle { get; set; }

    // Local read-only instrumentation, before the upstream reader seeks to
    // the next page. Allows rejecting unparsed/truncated item-page bodies.
    public int ReadBodyEndBytePosition { get; private set; }

    /// <summary>
    /// Reads a single stash tab from a byte span using the default external data.
    /// </summary>
    /// <param name="data">The stash tab data.</param>
    /// <returns>The parsed stash tab.</returns>
    public static D2StashTab Read(ReadOnlySpan<byte> data) => Read(data, TxtFileExternalData.Default);

    /// <summary>
    /// Reads a single stash tab from a byte span.
    /// </summary>
    /// <param name="data">The stash tab data.</param>
    /// <param name="externalData">External game data for parsing items.</param>
    /// <returns>The parsed stash tab.</returns>
    public static D2StashTab Read(ReadOnlySpan<byte> data, IExternalData externalData)
    {
        var reader = new BitReader(data);
        return Read(ref reader, externalData);
    }

    /// <summary>
    /// Reads a single stash tab from a BitReader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data for parsing items.</param>
    /// <returns>The parsed stash tab.</returns>
    public static D2StashTab Read(ref BitReader reader, IExternalData externalData)
    {
        var tab = new D2StashTab();
        int startPosition = reader.BytePosition;  // Record start

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid stash tab magic: 0x{magic:X8}, expected 0x{Magic:X8}");

        tab.StashFormat = reader.ReadUInt32();
        tab.SaveVersion = reader.ReadUInt32();
        tab.Gold = reader.ReadUInt32();
        ushort size = reader.ReadUInt16(); // Size field (not stored, used for validation)
        tab.Season = reader.ReadUInt16();
        tab.TabType = (StashTabType)reader.ReadByte();
        reader.ReadBytes(tab.Padding21);
        reader.ReadBytes(tab.WithdrawOnlyData);
        reader.ReadBytes(tab.Padding44);

        // Game forces tabType=0 for StashFormat < 2
        if (tab.StashFormat < 2)
            tab.TabType = StashTabType.Normal;

        int bodySize = size - (int)HeaderSize;

        switch (tab.TabType)
        {
            case StashTabType.Chronicle:
                tab.Chronicle = ChronicleSection.Read(ref reader, bodySize);
                break;

            default: // Normal and AdvancedStash
                tab.Items = ItemsSection.Read(ref reader, externalData, tab.SaveVersion);
                break;
        }

        tab.ReadBodyEndBytePosition = reader.BytePosition;
        // Seek to the correct position for the next tab
        reader.SetBytePosition(startPosition + size);

        return tab;
    }

    /// <summary>
    /// Calculates an estimated buffer size for writing this tab.
    /// Returns a conservative estimate that should be large enough.
    /// </summary>
    public int EstimateSize()
    {
        // Header is always 64 bytes
        int size = (int)HeaderSize;

        if (TabType == StashTabType.Chronicle && Chronicle != null)
        {
            // Magic(4) + Version(2) + 3 counts(6) + Reserved(8) + entries(10 each) + trailing
            size += 4 + 2 + 6 + 8;
            size += (Chronicle.SetEntries.Count + Chronicle.UniqueEntries.Count + Chronicle.RunewordEntries.Count) * 10;
            size += Chronicle.TrailingData.Length;
        }
        else
        {
            size += Items.EstimateSize();
        }

        // Add 20% buffer for safety
        return (int)(size * 1.2);
    }

    /// <summary>
    /// Writes this stash tab to a new byte array.
    /// </summary>
    /// <param name="externalData">External game data for writing items. Uses built-in data if null.</param>
    /// <param name="targetVersion">Optional target version for format conversion.</param>
    /// <returns>The serialized stash tab bytes.</returns>
    public byte[] ToBytes(IExternalData? externalData = null, uint? targetVersion = null)
    {
        byte[] buffer = new byte[EstimateSize()];
        var writer = new BitWriter(buffer);
        Write(ref writer, externalData ?? TxtFileExternalData.Default, targetVersion);
        return buffer.AsSpan(0, writer.BytesWritten).ToArray();
    }

    /// <summary>
    /// Writes a stash tab to a BitWriter.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data for writing items.</param>
    /// <param name="targetVersion">Optional target version for format conversion. If null, uses current Version.</param>
    public void Write(ref BitWriter writer, IExternalData externalData, uint? targetVersion = null)
    {
        uint writeVersion = targetVersion ?? SaveVersion;

        // Prepare for version conversion if needed (mutates data for new format)
        PrepareForVersion(SaveVersion, writeVersion);

        // Update Version permanently (data is now in the new format)
        SaveVersion = writeVersion;

        // Chronicle tabs don't exist in StashFormat < 2, skip entirely
        if (TabType == StashTabType.Chronicle && StashFormat < 2)
            return;

        int startPosition = writer.BytesWritten;

        writer.WriteUInt32(Magic);
        writer.WriteUInt32(StashFormat);
        writer.WriteUInt32(SaveVersion);
        writer.WriteUInt32(Gold);

        // Write placeholder for size, will update at end
        int sizeFieldPosition = writer.BitPosition;
        writer.WriteUInt16(0);
        writer.WriteUInt16(Season);
        writer.WriteByte((byte)TabType);
        writer.WriteBytes(Padding21);
        writer.WriteBytes(WithdrawOnlyData);
        writer.WriteBytes(Padding44);

        switch (TabType)
        {
            case StashTabType.Chronicle:
                Chronicle?.Write(ref writer);
                break;

            default: // Normal and AdvancedStash
                Items.Write(ref writer, externalData, writeVersion);
                break;
        }

        // Calculate this tab's size and write it back
        int tabSize = writer.BytesWritten - startPosition;
        int currentPosition = writer.BitPosition;
        writer.SetBitPosition(sizeFieldPosition);
        writer.WriteUInt16((ushort)tabSize);
        writer.SetBitPosition(currentPosition);
    }

    /// <summary>
    /// Prepares the save data for writing to a specific version format.
    /// Handles conversion between 1.14 (version 96) and D2R (version 97+) formats.
    /// </summary>
    private void PrepareForVersion(uint sourceVersion, uint targetVersion)
    {
        bool sourceIsD2R = sourceVersion > 96;
        bool targetIsD2R = targetVersion > 96;

        // Only convert if source and target formats differ
        if (!sourceIsD2R && targetIsD2R)
            Convert114ToD2R();

        // Upgrade StashFormat for v100+ (enables TabType)
        if (targetVersion >= 100)
        {
            if (StashFormat < 2)
                StashFormat = 2;
        }
        else if (targetVersion > 96 && StashFormat >= 2)
        {
            // Downgrade StashFormat for pre-v100 D2R
            StashFormat = 1;
            TabType = StashTabType.Normal;
        }
    }

    /// <summary>
    /// Converts save data from 1.14 format to D2R format.
    /// </summary>
    private void Convert114ToD2R()
    {
        // 3. Zero BodyLocation for stored items (D2R fails otherwise)
        foreach (var item in Items)
        {
            if (item.Position.Mode != ItemMode.Equipped)
                item.Position.BodyLocation = BodyLocation.None;
        }
    }
}
