using D2SSharp.Data;
using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Demon persistence section ("lf" magic, v>=103).
/// Contains a 92-byte demon descriptor and a stat list in "gf" format
/// with 32-bit raw values (no SaveAdd/ValShift encoding).
/// </summary>
public partial class DemonSection
{
    /// <summary>Section magic: "lf" (0x666C)</summary>
    public const ushort Magic = 0x666C;

    /// <summary>Section version (expected to be 1).</summary>
    public ushort SectionVersion { get; set; } = 1;

    /// <summary>Whether a demon exists.</summary>
    public bool HasDemon { get; set; }

    /// <summary>Padding byte after HasDemon (always written as 0, ignored by the reader).</summary>
    public byte Padding { get; set; }

    /// <summary>Demon data (null if no demon).</summary>
    public DemonData? DemonData { get; set; }

    /// <summary>
    /// Tries to read a demon section from a BitReader.
    /// Returns null if the section is not present (no matching magic bytes).
    /// </summary>
    public static DemonSection? TryRead(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        if (saveVersion < 103)
            return null;

        // Need at least 6 bytes for the header
        if (reader.BitsRemaining < 48)
            return null;

        // Peek at the next 4 bytes to check for section version + magic
        int savedPosition = reader.BitPosition;
        ushort version = reader.ReadUInt16();
        ushort magic = reader.ReadUInt16();

        if (magic != Magic || version != 1)
        {
            // Not a demon section, restore position
            reader.SetBitPosition(savedPosition);
            return null;
        }

        var section = new DemonSection
        {
            SectionVersion = version,
            HasDemon = reader.ReadByte() != 0,
            Padding = reader.ReadByte()
        };

        if (section.HasDemon)
        {
            section.DemonData = DemonData.Read(ref reader, externalData, saveVersion);
        }

        return section;
    }

    /// <summary>
    /// Writes a demon section to a BitWriter.
    /// </summary>
    public void Write(ref BitWriter writer, IExternalData externalData, uint saveVersion)
    {
        writer.WriteUInt16(SectionVersion);
        writer.WriteUInt16(Magic);
        writer.WriteByte((byte)(HasDemon ? 1 : 0));
        writer.WriteByte(Padding);

        if (HasDemon && DemonData != null)
        {
            DemonData.Write(ref writer, externalData, saveVersion);
        }
    }
}

/// <summary>
/// Data for a demon: a 92-byte descriptor struct followed by a
/// "gf"-format stat list with 32-bit raw values.
/// </summary>
/// <remarks>
/// Stat blob format (version >= 103):
/// Same as PlayerStats except values are always 32 bits (not CsvBits).
/// <code>
/// uint16 magic "gf" (0x6667)
/// bitstream: [9-bit statId] [CsvParam param] [32-bit value] ...
/// 9-bit terminator 0x1FF
/// padded to byte boundary
/// </code>
/// </remarks>
public partial class DemonData
{
    /// <summary>Demon type (Standard or SuperUnique).</summary>
    public DemonType DemonType { get; set; }

    /// <summary>Monster class row index (0-based).
    /// For <see cref="Enums.DemonType.Standard"/>: MonStats.txt row index.
    /// For <see cref="Enums.DemonType.SuperUnique"/>: SuperUniques.txt row index;
    /// the actual MonStats ID is derived from the SuperUniques record at runtime.
    /// Stored on wire as ClassId + 1 (1-based).</summary>
    public ushort ClassId { get; set; }

    /// <summary>Monster name seed.</summary>
    public ushort NameSeed { get; set; }

    /// <summary>Monster type flags (SuperUnique 0x02, Champion 0x04,
    /// Unique 0x08, Minion 0x10, empowered boss 0x200 and empowered boss minion 0x400).</summary>
    public ushort MonFlags { get; set; }

    /// <summary>Stat count for the legacy (save version &lt;= 94) bitmask stat format.
    /// The game always writes 16; unused for v&gt;=103.</summary>
    public ushort NumStats { get; set; } = 16;

    // Offsets 0x0C..0x43 mirror the game's 56-byte monster init data, used on load
    // to recreate the monster with its original spawn parameters. Padding fields are never written by the game and
    // contain uninitialized stack data; they are preserved for byte-exact round-trip.

    /// <summary>Difficulty of the game the demon was spawned in.</summary>
    public Difficulty Difficulty { get; set; }

    /// <summary>Game version of the game the demon was spawned in.</summary>
    public GameVersion GameVersion { get; set; }

    /// <summary>Struct padding (uninitialized data).</summary>
    public ushort Padding0E { get; set; }

    /// <summary>Non-zero if the game the demon was spawned in was a ladder game.</summary>
    public uint IsLadder { get; set; }

    /// <summary>
    /// Game type of the game the demon was spawned in (likely the game mode: 0 = single player,
    /// 3 = Battle.net, ...). When non-zero, or when <see cref="IsLadder"/> is non-zero, monster stats
    /// are calculated from the online MonLvl columns.
    /// </summary>
    public byte GameType { get; set; }

    /// <summary>Struct padding (uninitialized data).</summary>
    public byte[] Padding15 { get; set; } = new byte[3];

    /// <summary>Levels.txt ID of the area the demon originally spawned in.</summary>
    public uint LevelId { get; set; }

    /// <summary>Monster level used for stat calculation.</summary>
    public byte MonsterLevel { get; set; }

    /// <summary>Non-zero if the demon spawned in a terrorized area.</summary>
    public byte IsTerrorized { get; set; }

    /// <summary>Struct padding (uninitialized data).</summary>
    public ushort Padding1E { get; set; }

    /// <summary>Terror zone parameter of the spawn area (passed to monster stat calculation).</summary>
    public uint TerrorZoneParam { get; set; }

    /// <summary>Bonus added to <see cref="MonsterLevel"/> when the monster is recreated.</summary>
    public uint LevelBonus { get; set; }

    /// <summary>Bonus damage percent for empowered bosses and their minions (MonFlags 0x200/0x400).</summary>
    public uint BonusDamagePercent { get; set; }

    /// <summary>Player count HP percent bonus.</summary>
    public int PlayerCountHpBonus { get; set; }

    /// <summary>Player count experience percent bonus.</summary>
    public int PlayerCountExperienceBonus { get; set; }

    /// <summary>Player count monster skill level bonus.</summary>
    public int PlayerCountSkillBonus { get; set; }

    /// <summary>Player count bonus difficulty.</summary>
    public int PlayerCountDifficulty { get; set; }

    /// <summary>Player count (sets the monster_playercount stat, minimum 1).</summary>
    public int PlayerCount { get; set; }

    /// <summary>Monster graphic components (16 bytes).</summary>
    public byte[] Components { get; set; } = new byte[16];

    /// <summary>Monster unique modifiers (9 bytes, valid mods packed to the front).</summary>
    public byte[] MonUMod { get; set; } = new byte[9];

    /// <summary>Struct padding (uninitialized data).</summary>
    public byte[] Padding59 { get; set; } = new byte[3];

    /// <summary>Demon stats (32-bit raw values, no SaveAdd/ValShift transform).</summary>
    public List<Stat> Stats { get; set; } = [];

    /// <summary>
    /// Reads a demon from the bit stream: 92-byte struct + stat blob.
    /// </summary>
    public static DemonData Read(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        var demon = new DemonData();

        // Read 92-byte struct field by field
        ushort statDataSize = reader.ReadUInt16();                    // 0x00
        demon.DemonType = (DemonType)reader.ReadUInt16();             // 0x02
        demon.ClassId = (ushort)(reader.ReadUInt16() - 1);            // 0x04 (wire is 1-based)
        demon.NameSeed = reader.ReadUInt16();                         // 0x06
        demon.MonFlags = reader.ReadUInt16();                         // 0x08 0x00 or 0x16 - minion
        demon.NumStats = reader.ReadUInt16();                         // 0x0A
        demon.Difficulty = (Difficulty)reader.ReadByte();             // 0x0C
        demon.GameVersion = (GameVersion)reader.ReadByte();           // 0x0D
        demon.Padding0E = reader.ReadUInt16();                        // 0x0E
        demon.IsLadder = reader.ReadUInt32();                         // 0x10
        demon.GameType = reader.ReadByte();                           // 0x14
        reader.ReadBytes(demon.Padding15);                            // 0x15 (3 bytes)
        demon.LevelId = reader.ReadUInt32();                          // 0x18
        demon.MonsterLevel = reader.ReadByte();                       // 0x1C
        demon.IsTerrorized = reader.ReadByte();                       // 0x1D
        demon.Padding1E = reader.ReadUInt16();                        // 0x1E
        demon.TerrorZoneParam = reader.ReadUInt32();                  // 0x20
        demon.LevelBonus = reader.ReadUInt32();                       // 0x24
        demon.BonusDamagePercent = reader.ReadUInt32();               // 0x28
        demon.PlayerCountHpBonus = reader.ReadInt32();                // 0x2C
        demon.PlayerCountExperienceBonus = reader.ReadInt32();        // 0x30
        demon.PlayerCountSkillBonus = reader.ReadInt32();             // 0x34
        demon.PlayerCountDifficulty = reader.ReadInt32();             // 0x38
        demon.PlayerCount = reader.ReadInt32();                       // 0x3C
        reader.ReadBytes(demon.Components);                           // 0x40 (16 bytes)
        reader.ReadBytes(demon.MonUMod);                              // 0x50 (9 bytes)
        reader.ReadBytes(demon.Padding59);                            // 0x59 (3 bytes)

        // Read stat blob
        if (statDataSize > 0)
        {
            Span<byte> statBlob = stackalloc byte[statDataSize];
            reader.ReadBytes(statBlob);
            demon.Stats = ReadDemonStats(statBlob, externalData, saveVersion);
        }

        return demon;
    }

    /// <summary>
    /// Writes a demon to the bit stream: 92-byte struct + stat blob.
    /// </summary>
    public void Write(ref BitWriter writer, IExternalData externalData, uint saveVersion)
    {
        // Serialize stats to temp buffer first to determine statDataSize
        Span<byte> statBuffer = stackalloc byte[16384];
        var statDataSize = WriteDemonStats(statBuffer, externalData, saveVersion);

        // Write 92-byte struct
        writer.WriteUInt16((ushort)statDataSize);                     // 0x00
        writer.WriteUInt16((ushort)DemonType);                        // 0x02
        writer.WriteUInt16((ushort)(ClassId + 1));                    // 0x04 (wire is 1-based)
        writer.WriteUInt16(NameSeed);                                 // 0x06
        writer.WriteUInt16(MonFlags);                                 // 0x08
        writer.WriteUInt16(NumStats);                                 // 0x0A
        writer.WriteByte((byte)Difficulty);                           // 0x0C
        writer.WriteByte((byte)GameVersion);                          // 0x0D
        writer.WriteUInt16(Padding0E);                                // 0x0E
        writer.WriteUInt32(IsLadder);                                 // 0x10
        writer.WriteByte(GameType);                                   // 0x14
        writer.WriteBytes(Padding15);                                 // 0x15 (3 bytes)
        writer.WriteUInt32(LevelId);                                  // 0x18
        writer.WriteByte(MonsterLevel);                               // 0x1C
        writer.WriteByte(IsTerrorized);                               // 0x1D
        writer.WriteUInt16(Padding1E);                                // 0x1E
        writer.WriteUInt32(TerrorZoneParam);                          // 0x20
        writer.WriteUInt32(LevelBonus);                               // 0x24
        writer.WriteUInt32(BonusDamagePercent);                       // 0x28
        writer.WriteInt32(PlayerCountHpBonus);                        // 0x2C
        writer.WriteInt32(PlayerCountExperienceBonus);                // 0x30
        writer.WriteInt32(PlayerCountSkillBonus);                     // 0x34
        writer.WriteInt32(PlayerCountDifficulty);                     // 0x38
        writer.WriteInt32(PlayerCount);                               // 0x3C
        writer.WriteBytes(Components);                                // 0x40 (16 bytes)
        writer.WriteBytes(MonUMod);                                   // 0x50 (9 bytes)
        writer.WriteBytes(Padding59);                                 // 0x59 (3 bytes)

        // Write stat blob
        if (statDataSize > 0)
        {
            writer.WriteBytes(statBuffer[..statDataSize]);
        }
    }

    /// <summary>
    /// Parses demon stats from a "gf"-format blob with 32-bit raw values.
    /// </summary>
    private static List<Stat> ReadDemonStats(ReadOnlySpan<byte> statBlob, IExternalData externalData, uint saveVersion)
    {
        var stats = new List<Stat>();
        var statReader = new BitReader(statBlob);

        // Verify "gf" magic
        ushort magic = statReader.ReadUInt16();
        if (magic != PlayerStats.Magic)
            throw new InvalidDataException($"Invalid demon stat magic: 0x{magic:X4}, expected 0x{PlayerStats.Magic:X4}");

        // Read stats until terminator
        while (true)
        {
            StatId statId = (StatId)statReader.ReadBits(9);

            if (statId == StatId.Terminator)
                break;

            var statInfo = externalData.GetStatInfo(statId, saveVersion);

            // Demon stats use the "gf" (player stat) format, so CsvParam is the
            // correct param bit width here (not SaveParamBits which is for item stats).
            int layer = 0;
            if (statInfo.CsvParam > 0)
            {
                layer = statReader.ReadSignedBits(statInfo.CsvParam);
            }

            // Demon stats always use 32-bit raw values
            long value = statReader.ReadBits(32);

            stats.Add(new Stat { Id = statId, Layer = layer, Value = value });
        }

        return stats;
    }

    /// <summary>
    /// Serializes demon stats into a "gf"-format blob with 32-bit raw values.
    /// Returns the number of bytes written.
    /// </summary>
    private int WriteDemonStats(Span<byte> buffer, IExternalData externalData, uint saveVersion)
    {
        var statWriter = new BitWriter(buffer);

        // Write "gf" magic
        statWriter.WriteUInt16(PlayerStats.Magic);

        // Write each stat
        foreach (var stat in Stats)
        {
            var statInfo = externalData.GetStatInfo(stat.Id, saveVersion);

            statWriter.WriteBits((uint)stat.Id, 9);

            if (statInfo.CsvParam > 0)
            {
                statWriter.WriteBits((uint)stat.Layer, statInfo.CsvParam);
            }

            // Demon stats always use 32-bit raw values
            statWriter.WriteBits(checked((uint)stat.Value), 32);
        }

        // Write terminator
        statWriter.WriteBits((uint)StatId.Terminator, 9);

        // Align to byte boundary
        statWriter.AlignToByte();

        return statWriter.BytesWritten;
    }
}
