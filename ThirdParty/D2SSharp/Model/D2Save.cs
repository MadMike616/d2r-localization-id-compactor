using D2SSharp.Data;
using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Complete Diablo 2 save file model.
/// </summary>
public partial class D2Save
{
    /// <summary>File magic value: 0xAA55AA55</summary>
    public const uint Magic = 0xAA55AA55;

    /// <summary>Current save version (105 for D2R 3.x).</summary>
    public const uint CurrentVersion = 105;

    /// <summary>Save file version.</summary>
    public uint Version { get; set; } = CurrentVersion;

    /// <summary>Total file size in bytes (updated on write).</summary>
    public uint FileSize { get; set; }

    /// <summary>File checksum (updated on write).</summary>
    public uint Checksum { get; set; }

    /// <summary>Character section.</summary>
    public Character Character { get; set; } = new();

    /// <summary>Quest data section (298 bytes).</summary>
    public QuestSection Quests { get; set; } = new();

    /// <summary>Waypoint data section (80 bytes).</summary>
    public WaypointSection Waypoints { get; set; } = new();

    /// <summary>Player intro section (52 bytes).</summary>
    public PlayerIntroSection PlayerIntro { get; set; } = new();

    /// <summary>Player stats section (variable length).</summary>
    public PlayerStats Stats { get; set; } = new();

    /// <summary>Skills section (variable length).</summary>
    public SkillsSection Skills { get; set; } = new();

    /// <summary>Player items section.</summary>
    public ItemsSection Items { get; set; } = new();

    /// <summary>Corpse section.</summary>
    public CorpseSection Corpses { get; set; } = new();

    /// <summary>Mercenary items section (expansion only).</summary>
    public MercItemsSection? MercItems { get; set; }

    /// <summary>Iron golem section (expansion only).</summary>
    public IronGolemSection? IronGolem { get; set; }

    /// <summary>Demon section (v>=103, optional).</summary>
    public DemonSection? Demon { get; set; }

    /// <summary>
    /// Trailing data after all known sections (mod-specific extensions).
    /// Preserved during round-trip for compatibility with modded saves.
    /// </summary>
    public byte[] TrailingData { get; set; } = [];

    /// <summary>
    /// Reads a save file from a byte span using the default external data.
    /// </summary>
    /// <param name="data">The save file data.</param>
    /// <returns>The parsed save file.</returns>
    public static D2Save Read(ReadOnlySpan<byte> data) => Read(data, TxtFileExternalData.Default);

    /// <summary>
    /// Reads a save file from a byte span.
    /// </summary>
    /// <param name="data">The save file data.</param>
    /// <param name="externalData">External game data for parsing stats/items.</param>
    /// <returns>The parsed save file.</returns>
    public static D2Save Read(ReadOnlySpan<byte> data, IExternalData externalData)
    {
        var reader = new BitReader(data);
        var save = new D2Save();

        // Read file header
        uint magic = reader.ReadUInt32();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid save file magic: 0x{magic:X8}, expected 0x{Magic:X8}");

        save.Version = reader.ReadUInt32();
        save.FileSize = reader.ReadUInt32();
        save.Checksum = reader.ReadUInt32();

        // Read fixed sections (pass version for format selection)
        save.Character = Character.Read(ref reader, save.Version);
        save.Quests = QuestSection.Read(ref reader);
        save.Waypoints = WaypointSection.Read(ref reader);
        save.PlayerIntro = PlayerIntroSection.Read(ref reader);

        // Read variable sections (pass save.Version for format selection)
        save.Stats = PlayerStats.Read(ref reader, externalData, save.Version);
        save.Skills = SkillsSection.Read(ref reader, save.Character.Class, save.Character.NumSkills);
        save.Items = ItemsSection.Read(ref reader, externalData, save.Version);

        save.Corpses = CorpseSection.Read(ref reader, externalData, save.Version);

        // Expansion-only sections
        if (save.IsExpansion(save.Version))
        {
            save.MercItems = MercItemsSection.Read(ref reader, externalData, save.Character.MercData.HasMerc, save.Version);
            save.IronGolem = IronGolemSection.Read(ref reader, externalData, save.Version);

            // Try to read demon section (v>=103, optional)
            if (save.Character.Preview.GameVersion == GameVersion.ReignOfTheWarlock)
                save.Demon = DemonSection.TryRead(ref reader, externalData, save.Version);
        }

        // Capture any remaining bytes (mod-specific trailing data)
        int remainingBytes = reader.BitsRemaining >> 3;
        if (remainingBytes > 0)
        {
            save.TrailingData = new byte[remainingBytes];
            reader.ReadBytes(save.TrailingData);
        }

        return save;
    }

    /// <summary>
    /// Writes a save file to a new byte array.
    /// </summary>
    /// <param name="externalData">External game data for writing stats/items. Uses built-in data if null.</param>
    /// <param name="targetVersion">Optional target version for format conversion.</param>
    /// <returns>The serialized save file bytes.</returns>
    public byte[] ToBytes(IExternalData? externalData = null, uint? targetVersion = null)
    {
        byte[] buffer = new byte[EstimateSize()];
        int bytesWritten = Write(buffer, externalData ?? TxtFileExternalData.Default, targetVersion);
        return buffer.AsSpan(0, bytesWritten).ToArray();
    }

    /// <summary>
    /// Writes a save file to a byte span using the default external data.
    /// </summary>
    /// <param name="buffer">The buffer to write to (must be large enough).</param>
    /// <param name="targetVersion">Optional target version for format conversion. If null, uses current Version.</param>
    /// <returns>The number of bytes written.</returns>
    public int Write(Span<byte> buffer, uint? targetVersion = null) => Write(buffer, TxtFileExternalData.Default, targetVersion);

    /// <summary>
    /// Writes a save file to a byte span.
    /// </summary>
    /// <param name="buffer">The buffer to write to (must be large enough).</param>
    /// <param name="externalData">External game data for writing stats/items.</param>
    /// <param name="targetVersion">Optional target version for format conversion. If null, uses current Version.</param>
    /// <returns>The number of bytes written.</returns>
    public int Write(Span<byte> buffer, IExternalData externalData, uint? targetVersion = null)
    {
        uint writeVersion = targetVersion ?? Version;

        // Prepare for version conversion if needed (mutates data for new format)
        PrepareForVersion(Version, writeVersion);

        // Update Version permanently (data is now in the new format)
        Version = writeVersion;

        var writer = new BitWriter(buffer);

        // Write file header (placeholders for size/checksum, updated at end)
        writer.WriteUInt32(Magic);
        writer.WriteUInt32(Version);
        writer.WriteUInt32(0); // FileSize placeholder
        writer.WriteUInt32(0); // Checksum placeholder

        // Write fixed sections (pass version for format selection)
        Character.Write(ref writer, Version);
        Quests.Write(ref writer);
        Waypoints.Write(ref writer);
        PlayerIntro.Write(ref writer);

        // Write variable sections (pass Version for format selection)
        Stats.Write(ref writer, externalData, Version);
        Skills.Write(ref writer);
        Items.Write(ref writer, externalData, Version);
        Corpses.Write(ref writer, externalData, Version);

        // Expansion-only sections
        if (IsExpansion(Version))
        {
            (MercItems ?? new MercItemsSection()).Write(ref writer, externalData, Character.MercData.HasMerc, Version);
            (IronGolem ?? new IronGolemSection()).Write(ref writer, externalData, Version);

            // Write demon section if present
            if (Character.Preview.GameVersion == GameVersion.ReignOfTheWarlock)
                Demon?.Write(ref writer, externalData, Version);
        }

        // Write trailing data (mod-specific extensions)
        if (TrailingData.Length > 0)
        {
            writer.WriteBytes(TrailingData);
        }

        int bytesWritten = writer.BytesWritten;

        // Update file size in header
        BitConverter.TryWriteBytes(buffer.Slice(8, 4), (uint)bytesWritten);

        // Update checksum
        ChecksumCalculator.Update(buffer[..bytesWritten]);

        return bytesWritten;
    }

    /// <summary>
    /// Calculates an estimated buffer size for writing.
    /// Returns a conservative estimate that should be large enough for most saves.
    /// </summary>
    /// <returns>Estimated buffer size in bytes.</returns>
    public int EstimateSize()
    {
        // Base: file header (16) + character (version-dependent) + quest (298) + waypoint (80) + intro (52)
        int charSize = Character.GetSize(Version);
        int size = 16 + charSize + 298 + 80 + 52;

        // Stats: ~33 bytes typical (16 stats * ~15 bits average + header)
        size += 50;

        // Skills: 2 + numSkills
        size += 2 + Character.NumSkills;

        // Items
        size += Items.EstimateSize();

        // Corpse: header + corpses with items
        size += 4;
        foreach (var corpse in Corpses)
        {
            size += 12 + corpse.Items.EstimateSize();
        }

        // Expansion sections
        if (IsExpansion(Version))
        {
            // Merc items: magic(2) + items section
            size += 2 + (MercItems?.Items ?? new ItemsSection()).EstimateSize();

            // Iron golem
            size += 3;
            if (IronGolem?.GolemItem != null)
                size += 200; // Single item estimate

            // Demon section
            if (Character.Preview.GameVersion == GameVersion.ReignOfTheWarlock && Demon != null)
            {
                size += 6; // header
                if (Demon.DemonData != null)
                {
                    // 92-byte struct + estimated stat blob
                    size += 92 + 4 + Demon.DemonData.Stats.Count * 8;
                }
            }
        }

        // Trailing data (mod-specific extensions)
        size += TrailingData.Length;

        // Add 20% buffer for safety
        return (int)(size * 1.2);
    }

    /// <summary>
    /// Verifies the checksum of save file data.
    /// </summary>
    public static bool VerifyChecksum(ReadOnlySpan<byte> data)
    {
        return ChecksumCalculator.Verify(data);
    }

    /// <summary>
    /// Returns true if this save is an expansion save, checking GameVersion for v104+ or CharacterFlags for older versions.
    /// </summary>
    private bool IsExpansion(uint version) => version >= 104
        ? Character.Preview.GameVersion > GameVersion.Classic
        : Character.Flags.HasFlag(CharacterFlags.Expansion);

    /// <summary>
    /// Prepares the save data for writing to a specific version format.
    /// Handles conversion between 1.14 (version 96) and D2R (version 97+) formats,
    /// and between v&lt;=103 and v>=104 header formats.
    /// </summary>
    private void PrepareForVersion(uint sourceVersion, uint targetVersion)
    {
        bool sourceIsD2R = sourceVersion > 96;
        bool targetIsD2R = targetVersion > 96;

        // Handle 1.14 <-> D2R conversion
        if (!sourceIsD2R && targetIsD2R)
            Convert114ToD2R();
        else if (sourceIsD2R && !targetIsD2R)
            ConvertD2RTo114();

        // Handle v<=103 <-> v>=104 header format conversion
        bool sourceIsNewHeader = sourceVersion >= 104;
        bool targetIsNewHeader = targetVersion >= 104;

        if (!sourceIsNewHeader && targetIsNewHeader)
            Convert103To104();
        else if (sourceIsNewHeader && !targetIsNewHeader)
            Convert104To103();
    }

    /// <summary>
    /// Converts save data from 1.14 format to D2R format.
    /// </summary>
    private void Convert114ToD2R()
    {
        // 1. Copy name to Preview.Name
        Character.Preview.Name = Character.Name;
        Character.Name = "";

        // 2. Populate Preview items ONLY for the 4 visible positions
        // Note: Preview.LeftHand/RightHand are swapped relative to BodyLocation (character screen perspective)
        foreach (var item in Items)
        {
            if (item.Position.Mode != ItemMode.Equipped) continue;

            switch (item.Position.BodyLocation)
            {
                case BodyLocation.RightArm:
                    // Primary weapon slot gets Targeting flag
                    Character.Preview.LeftHand = CreatePreviewItem(item, Composits.LeftHand, ItemFlags.Targeting);
                    break;
                case BodyLocation.LeftArm:
                    Character.Preview.RightHand = CreatePreviewItem(item, Composits.RightHand);
                    break;
                case BodyLocation.Torso:
                    Character.Preview.Torso = CreatePreviewItem(item, Composits.Torso);
                    break;
                case BodyLocation.Head:
                    Character.Preview.Head = CreatePreviewItem(item, Composits.Head);
                    break;
            }
        }

        // 3. Zero BodyLocation for stored items (D2R fails otherwise)
        foreach (var item in Items)
        {
            if (item.Position.Mode != ItemMode.Equipped)
                item.Position.BodyLocation = BodyLocation.None;
        }
    }

    /// <summary>
    /// Converts save data from D2R format to 1.14 format.
    /// </summary>
    private void ConvertD2RTo114()
    {
        // 1. Copy name from Preview.Name
        Character.Name = Character.Preview.Name;
        Character.Preview.Name = "";

        // 2. Zero Preview items (1.14 doesn't use them)
        Character.Preview.LeftHand = new PreviewItem();
        Character.Preview.RightHand = new PreviewItem();
        Character.Preview.Torso = new PreviewItem();
        Character.Preview.Head = new PreviewItem();

        // BodyLocation: keep as-is (1.14 handles None fine)
    }

    /// <summary>
    /// Converts from old header format (v&lt;=103) to new header format (v>=104).
    /// </summary>
    private void Convert103To104()
    {
        // Name: Character.Name -> PreviewData.Name (if not already done by Convert114ToD2R)
        if (!string.IsNullOrEmpty(Character.Name))
        {
            Character.Preview.Name = Character.Name;
            Character.Name = "";
        }

        // GameVersion: same rule as the game's v<=103 reader, keep a valid stored value (1-3),
        // otherwise derive it from CharacterFlags
        if (Character.Preview.GameVersion is < GameVersion.Classic or > GameVersion.ReignOfTheWarlock)
            Character.Preview.GameVersion = Character.Flags.HasFlag(CharacterFlags.Expansion) ? GameVersion.Expansion : GameVersion.Classic;
        Character.Flags &= ~CharacterFlags.Expansion;

        // SaveTimes/Experiences: slots 0-1 already populated, 2-5 remain zero (default)
    }

    /// <summary>
    /// Converts from new header format (v>=104) to old header format (v&lt;=103).
    /// </summary>
    private void Convert104To103()
    {
        // Name: PreviewData.Name -> Character.Name (truncate to 15 chars)
        if (string.IsNullOrEmpty(Character.Name) && !string.IsNullOrEmpty(Character.Preview.Name))
        {
            Character.Name = Character.Preview.Name.Length > 15
                ? Character.Preview.Name[..15]
                : Character.Preview.Name;
        }

        if (Character.Preview.GameVersion > GameVersion.Classic)
        {
            Character.Flags |= CharacterFlags.Expansion;
        }

        // SaveTimes/Experiences: only [0] and [1] are used in old format
        // Zero out slots 2-5
        for (int i = 2; i < 6; i++)
        {
            Character.Preview.SaveTimes[i] = 0;
            Character.Preview.Experiences[i] = 0;
        }
    }

    /// <summary>
    /// Creates a PreviewItem from an equipped item.
    /// </summary>
    /// <param name="item">The equipped item.</param>
    /// <param name="composit">The composit index for Transform lookup.</param>
    /// <param name="flags">Optional flags to set on the preview item.</param>
    private PreviewItem CreatePreviewItem(Item item, Composits composit, ItemFlags flags = 0)
    {
        // Extract FileIndex from quality data if available
        ushort fileIndex = item.QualityData?.FileIndex ?? 0;

        return new PreviewItem
        {
            Code = item.ItemCode,
            Quality = item.Quality,
            Flags = flags,
            Transform = Character.AppearanceTints[(int)composit],
            FileIndex = fileIndex
        };
    }
}
