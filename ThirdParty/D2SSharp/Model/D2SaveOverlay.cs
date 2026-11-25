using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using D2SSharp.Enums;

namespace D2SSharp.Model;

/// <summary>
/// File header layout (16 bytes at offset 0x00).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct HeaderLayout
{
    private const int Size = 16;
    internal const int FileOffset = 0;

    [FieldOffset(0)] public uint Magic;
    [FieldOffset(4)] public uint Version;
    [FieldOffset(8)] public uint FileSize;
    [FieldOffset(12)] public uint Checksum;

    internal readonly bool IsValid => Magic == D2Save.Magic;

    /// <summary>
    /// Returns the save file version from raw bytes without allocating.
    /// </summary>
    /// <param name="data">The save file data (minimum 8 bytes).</param>
    /// <returns>The save file version.</returns>
    public static uint GetVersion(ReadOnlySpan<byte> data)
    {
        ref readonly var header = ref MemoryMarshal.AsRef<HeaderLayout>(data);
        if (!header.IsValid)
            throw new InvalidDataException($"Invalid save file magic: 0x{header.Magic:X8}, expected 0x{D2Save.Magic:X8}");
        return header.Version;
    }
}

/// <summary>
/// Mercenary data layout (16 bytes within Character section).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct MercDataLayout
{
    private const int Size = 16;

    [FieldOffset(0)] public uint Flags;
    [FieldOffset(4)] public uint Seed;
    [FieldOffset(8)] public ushort NameIndex;
    [FieldOffset(10)] public ushort HirelingId;
    [FieldOffset(12)] public uint Experience;

    public bool IsDead
    {
        readonly get => (Flags & 0x00010000) != 0;
        set => Flags = value ? Flags | 0x00010000u : Flags & ~0x00010000u;
    }

    public readonly bool HasMerc => Seed != 0 || NameIndex != 0 || Experience != 0;
}

/// <summary>
/// Preview item layout (12 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct PreviewItemLayout
{
    private const int Size = 12;

    [FieldOffset(0)] public uint Code;
    [FieldOffset(4)] public byte Transform;
    [FieldOffset(5)] public ItemQuality Quality;
    [FieldOffset(6)] public ushort FileIndex;
    [FieldOffset(8)] public ItemFlags Flags;

    public string CodeString
    {
        readonly get
        {
            Span<byte> bytes = stackalloc byte[4];
            MemoryMarshal.Write(bytes, in Code);
            int len = bytes.IndexOf((byte)0);
            if (len < 0) len = 4;
            while (len > 0 && bytes[len - 1] == ' ') len--;
            return Encoding.ASCII.GetString(bytes[..len]);
        }
        set
        {
            Span<byte> bytes = stackalloc byte[4];
            bytes.Clear();
            Encoding.ASCII.GetBytes(value.AsSpan()[..Math.Min(value.Length, 4)], bytes);
            Code = MemoryMarshal.Read<uint>(bytes);
        }
    }
}

/// <summary>
/// Preview data layout (144 bytes within Character section).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct PreviewDataLayout
{
    private const int Size = 144;

    [FieldOffset(0)] public ulong ExpansionSaveTime;
    [FieldOffset(8)] public ulong ClassicSaveTime;
    [FieldOffset(16)] public byte GuildEmblemColor;
    [FieldOffset(17)] public GameVersion GameVersion;
    [FieldOffset(18)] public ushort PreviewPadding;
    [FieldOffset(20)] public uint ExpansionExperience;
    [FieldOffset(24)] public uint ClassicExperience;
    [FieldOffset(28)] public PreviewItemLayout LeftHand;
    [FieldOffset(40)] public PreviewItemLayout RightHand;
    [FieldOffset(52)] public PreviewItemLayout Torso;
    [FieldOffset(64)] public PreviewItemLayout Head;
    [FieldOffset(76)] private fixed byte _name[60];
    [FieldOffset(136)] public ulong Reserved;

    /// <summary>Internal name field access for D2SaveLayout.Name property.</summary>
    internal string Name
    {
        get
        {
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref _name[0], 60);
            int nullIndex = span.IndexOf((byte)0);
            if (nullIndex < 0) nullIndex = 60;
            return Encoding.UTF8.GetString(span[..nullIndex]);
        }
        set
        {
            Span<byte> span = MemoryMarshal.CreateSpan(ref _name[0], 60);
            span.Clear();
            Encoding.UTF8.GetBytes(value.AsSpan()[..Math.Min(value.Length, 59)], span);
        }
    }
}

/// <summary>
/// Skill hotkey layout (4 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct SkillLayout
{
    private const int Size = 4;

    [FieldOffset(0)] private ushort _skillData;
    [FieldOffset(2)] public ushort ItemIndex;

    public ushort SkillId
    {
        readonly get => (ushort)(_skillData & 0x7FFF);
        set => _skillData = (ushort)((_skillData & 0x8000) | (value & 0x7FFF));
    }

    public Side Side
    {
        readonly get => (Side)(_skillData >> 15);
        set => _skillData = (ushort)((_skillData & 0x7FFF) | ((int)value << 15));
    }
}

/// <summary>
/// Character section layout (319 bytes at offset 0x10).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct CharacterLayout
{
    private const int Size = 319;
    internal const int FileOffset = 0x10;

    [FieldOffset(0)] public uint WeaponSwitch;
    [FieldOffset(4)] private fixed byte _name[16];
    [FieldOffset(20)] public CharacterFlags Flags;
    [FieldOffset(24)] public CharacterClass Class;
    [FieldOffset(25)] public byte NumStats;
    [FieldOffset(26)] public byte NumSkills;
    [FieldOffset(27)] public byte Level;
    [FieldOffset(28)] public uint CreateTime;
    [FieldOffset(32)] public uint LastSaveTime;
    [FieldOffset(36)] public uint PlayTime;
    [FieldOffset(40)] private fixed byte _skillHotkeys[64];
    [FieldOffset(104)] public SkillLayout LeftSkill;
    [FieldOffset(108)] public SkillLayout RightSkill;
    [FieldOffset(112)] public SkillLayout SwitchLeftSkill;
    [FieldOffset(116)] public SkillLayout SwitchRightSkill;
    [FieldOffset(120)] private fixed byte _appearanceGraphics[16];
    [FieldOffset(136)] private fixed byte _appearanceTints[16];
    [FieldOffset(152)] private fixed byte _townDifficulty[3];
    [FieldOffset(155)] public uint MapSeed;
    [FieldOffset(159)] public MercDataLayout MercData;
    [FieldOffset(175)] public PreviewDataLayout Preview;

    /// <summary>Internal name field access for D2SaveLayout.Name property.</summary>
    internal string Name
    {
        get
        {
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref _name[0], 16);
            int nullIndex = span.IndexOf((byte)0);
            if (nullIndex < 0) nullIndex = 16;
            return Encoding.ASCII.GetString(span[..nullIndex]);
        }
        set
        {
            Span<byte> span = MemoryMarshal.CreateSpan(ref _name[0], 16);
            span.Clear();
            Encoding.ASCII.GetBytes(value.AsSpan()[..Math.Min(value.Length, 15)], span);
        }
    }

    /// <summary>Skill hotkeys (16 slots).</summary>
    public Span<SkillLayout> SkillHotkeys => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, SkillLayout>(ref _skillHotkeys[0]), 16);

    /// <summary>Appearance graphics (16 bytes).</summary>
    public Span<byte> AppearanceGraphics => MemoryMarshal.CreateSpan(ref _appearanceGraphics[0], 16);

    /// <summary>Appearance tints/colors (16 bytes).</summary>
    public Span<byte> AppearanceTints => MemoryMarshal.CreateSpan(ref _appearanceTints[0], 16);

    /// <summary>Current town per difficulty (Normal, NM, Hell).</summary>
    public Span<byte> TownDifficulty => MemoryMarshal.CreateSpan(ref _townDifficulty[0], 3);

    // Convenience flag accessors
    public bool IsHardcore
    {
        readonly get => (Flags & CharacterFlags.Hardcore) != 0;
        set => Flags = value ? Flags | CharacterFlags.Hardcore : Flags & ~CharacterFlags.Hardcore;
    }

    public bool IsDead
    {
        readonly get => (Flags & CharacterFlags.Dead) != 0;
        set => Flags = value ? Flags | CharacterFlags.Dead : Flags & ~CharacterFlags.Dead;
    }

    public bool IsExpansion
    {
        readonly get => (Flags & CharacterFlags.Expansion) != 0;
        set => Flags = value ? Flags | CharacterFlags.Expansion : Flags & ~CharacterFlags.Expansion;
    }

    public bool IsLadder
    {
        readonly get => (Flags & CharacterFlags.Ladder) != 0;
        set => Flags = value ? Flags | CharacterFlags.Ladder : Flags & ~CharacterFlags.Ladder;
    }
}

/// <summary>
/// Preview data layout for v>=104 saves (228 bytes within Character section).
/// v104+ expands to 6 save times, 6 experiences, GameVersion byte, and 96-byte name.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct PreviewDataLayoutV104
{
    private const int Size = 228;

    [FieldOffset(0x00)] private fixed ulong _saveTimes[6];
    [FieldOffset(0x30)] private fixed uint _experiences[6];
    [FieldOffset(0x48)] public byte GuildEmblemColor;
    [FieldOffset(0x49)] public GameVersion GameVersion;
    [FieldOffset(0x4A)] public ushort PreviewPadding;
    [FieldOffset(0x4C)] public PreviewItemLayout LeftHand;
    [FieldOffset(0x58)] public PreviewItemLayout RightHand;
    [FieldOffset(0x64)] public PreviewItemLayout Torso;
    [FieldOffset(0x70)] public PreviewItemLayout Head;
    [FieldOffset(0x7C)] private fixed byte _name[96];
    [FieldOffset(0xDC)] public ulong Reserved;

    /// <summary>Save timestamps (6 slots, Windows FILETIME).</summary>
    public Span<ulong> SaveTimes => MemoryMarshal.CreateSpan(ref _saveTimes[0], 6);

    /// <summary>Experience values (6 slots).</summary>
    public Span<uint> Experiences => MemoryMarshal.CreateSpan(ref _experiences[0], 6);

    /// <summary>Character name (UTF-8, max 96 bytes).</summary>
    internal string Name
    {
        get
        {
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref _name[0], 96);
            int nullIndex = span.IndexOf((byte)0);
            if (nullIndex < 0) nullIndex = 96;
            return Encoding.UTF8.GetString(span[..nullIndex]);
        }
        set
        {
            Span<byte> span = MemoryMarshal.CreateSpan(ref _name[0], 96);
            span.Clear();
            Encoding.UTF8.GetBytes(value.AsSpan()[..Math.Min(value.Length, 95)], span);
        }
    }

}

/// <summary>
/// Character section layout for v>=104 saves (387 bytes at offset 0x10).
/// v104+ removes the 16-byte Name field and uses PreviewDataLayoutV104 (228 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct CharacterLayoutV104
{
    private const int Size = 387;
    internal const int FileOffset = 0x10;

    [FieldOffset(0x00)] public uint WeaponSwitch;
    [FieldOffset(0x04)] public CharacterFlags Flags;
    [FieldOffset(0x08)] public CharacterClass Class;
    [FieldOffset(0x09)] public byte NumStats;
    [FieldOffset(0x0A)] public byte NumSkills;
    [FieldOffset(0x0B)] public byte Level;
    [FieldOffset(0x0C)] public uint CreateTime;
    [FieldOffset(0x10)] public uint LastSaveTime;
    [FieldOffset(0x14)] public uint PlayTime;
    [FieldOffset(0x18)] private fixed byte _skillHotkeys[64];
    [FieldOffset(0x58)] public SkillLayout LeftSkill;
    [FieldOffset(0x5C)] public SkillLayout RightSkill;
    [FieldOffset(0x60)] public SkillLayout SwitchLeftSkill;
    [FieldOffset(0x64)] public SkillLayout SwitchRightSkill;
    [FieldOffset(0x68)] private fixed byte _appearanceGraphics[16];
    [FieldOffset(0x78)] private fixed byte _appearanceTints[16];
    [FieldOffset(0x88)] private fixed byte _townDifficulty[3];
    [FieldOffset(0x8B)] public uint MapSeed;
    [FieldOffset(0x8F)] public MercDataLayout MercData;
    [FieldOffset(0x9F)] public PreviewDataLayoutV104 Preview;

    /// <summary>Skill hotkeys (16 slots).</summary>
    public Span<SkillLayout> SkillHotkeys => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, SkillLayout>(ref _skillHotkeys[0]), 16);

    /// <summary>Appearance graphics (16 bytes).</summary>
    public Span<byte> AppearanceGraphics => MemoryMarshal.CreateSpan(ref _appearanceGraphics[0], 16);

    /// <summary>Appearance tints/colors (16 bytes).</summary>
    public Span<byte> AppearanceTints => MemoryMarshal.CreateSpan(ref _appearanceTints[0], 16);

    /// <summary>Current town per difficulty (Normal, NM, Hell).</summary>
    public Span<byte> TownDifficulty => MemoryMarshal.CreateSpan(ref _townDifficulty[0], 3);

    // Convenience flag accessors
    public bool IsHardcore
    {
        readonly get => (Flags & CharacterFlags.Hardcore) != 0;
        set => Flags = value ? Flags | CharacterFlags.Hardcore : Flags & ~CharacterFlags.Hardcore;
    }

    public bool IsDead
    {
        readonly get => (Flags & CharacterFlags.Dead) != 0;
        set => Flags = value ? Flags | CharacterFlags.Dead : Flags & ~CharacterFlags.Dead;
    }

    public bool IsExpansion
    {
        readonly get => (Flags & CharacterFlags.Expansion) != 0;
        set => Flags = value ? Flags | CharacterFlags.Expansion : Flags & ~CharacterFlags.Expansion;
    }

    public bool IsLadder
    {
        readonly get => (Flags & CharacterFlags.Ladder) != 0;
        set => Flags = value ? Flags | CharacterFlags.Ladder : Flags & ~CharacterFlags.Ladder;
    }
}

/// <summary>
/// Quest data per difficulty (96 bytes).
/// Acts I-IV: 8 slots × 2 bytes = 16 bytes each.
/// Act V: 16 slots × 2 bytes = 32 bytes.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct QuestsDifficultyLayout
{
    private const int Size = 96;

    [FieldOffset(0)] private fixed byte _actI[16];
    [FieldOffset(16)] private fixed byte _actII[16];
    [FieldOffset(32)] private fixed byte _actIII[16];
    [FieldOffset(48)] private fixed byte _actIV[16];
    [FieldOffset(64)] private fixed byte _actV[32];

    public Span<QuestFlags> ActI => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, QuestFlags>(ref _actI[0]), 8);
    public Span<QuestFlags> ActII => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, QuestFlags>(ref _actII[0]), 8);
    public Span<QuestFlags> ActIII => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, QuestFlags>(ref _actIII[0]), 8);
    public Span<QuestFlags> ActIV => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, QuestFlags>(ref _actIV[0]), 8);
    public Span<QuestFlags> ActV => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, QuestFlags>(ref _actV[0]), 16);

    /// <summary>Gets quest flags by act (1-5) and slot index.</summary>
    public readonly QuestFlags GetQuest(int act, int slot)
    {
        ReadOnlySpan<QuestFlags> quests = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<byte, QuestFlags>(ref Unsafe.AsRef(in _actI[0])), 48);
        int index = act switch
        {
            1 => slot,
            2 => 8 + slot,
            3 => 16 + slot,
            4 => 24 + slot,
            5 => 32 + slot,
            _ => throw new ArgumentOutOfRangeException(nameof(act))
        };
        return quests[index];
    }

    /// <summary>Sets quest flags by act (1-5) and slot index.</summary>
    public void SetQuest(int act, int questIndex, QuestFlags flags)
    {
        Span<QuestFlags> quests = MemoryMarshal.CreateSpan(ref Unsafe.As<byte, QuestFlags>(ref _actI[0]), 48);
        int index = act switch
        {
            1 => questIndex,
            2 => 8 + questIndex,
            3 => 16 + questIndex,
            4 => 24 + questIndex,
            5 => 32 + questIndex,
            _ => throw new ArgumentOutOfRangeException(nameof(act))
        };
        quests[index] = flags;
    }
}

/// <summary>
/// Quest section layout (298 bytes at offset 0x14F).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct QuestSectionLayout
{
    private const int Size = 298;
    internal const int FileOffset = 0x14F;

    [FieldOffset(0)] public uint Magic;
    [FieldOffset(4)] public uint Version;
    [FieldOffset(8)] public ushort SectionSize;
    [FieldOffset(10)] public QuestsDifficultyLayout Normal;
    [FieldOffset(106)] public QuestsDifficultyLayout Nightmare;
    [FieldOffset(202)] public QuestsDifficultyLayout Hell;

    internal readonly bool IsValid => Magic == QuestSection.Magic;
}

/// <summary>
/// Waypoint data per difficulty (24 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct WaypointDifficultyLayout
{
    private const int Size = 24;

    /// <summary>Waypoint record version (0x0102). The game resets all waypoints on load if it differs.</summary>
    [FieldOffset(0)] public ushort Version;
    [FieldOffset(2)] private fixed byte _waypointBits[14];
    [FieldOffset(16)] private fixed byte _reserved[8];

    /// <summary>Raw waypoint bitfield (14 bytes, 112 bits; vanilla uses the first 39).</summary>
    public Span<byte> WaypointBits => MemoryMarshal.CreateSpan(ref _waypointBits[0], 14);

    /// <summary>Reserved bytes (8 bytes, always written as zero and never read by the game).</summary>
    public Span<byte> Reserved => MemoryMarshal.CreateSpan(ref _reserved[0], 8);

    /// <summary>Checks if a specific waypoint is activated by global index (0-38).</summary>
    public readonly bool IsWaypointActive(int waypointIndex)
    {
        if (waypointIndex < 0 || waypointIndex > 38) return false;
        int byteIndex = waypointIndex / 8;
        int bitIndex = waypointIndex % 8;
        return (_waypointBits[byteIndex] & (1 << bitIndex)) != 0;
    }

    /// <summary>Sets or clears a specific waypoint by global index (0-38).</summary>
    public void SetWaypoint(int waypointIndex, bool active)
    {
        if (waypointIndex < 0 || waypointIndex > 38) return;
        int byteIndex = waypointIndex / 8;
        int bitIndex = waypointIndex % 8;

        if (active)
            _waypointBits[byteIndex] |= (byte)(1 << bitIndex);
        else
            _waypointBits[byteIndex] &= (byte)~(1 << bitIndex);
    }

    /// <summary>Unlocks all waypoints for this difficulty.</summary>
    public void UnlockAllWaypoints()
    {
        for (int i = 0; i < 39; i++)
            SetWaypoint(i, true);
    }
}

/// <summary>
/// Waypoint section layout (80 bytes at offset 0x279).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct WaypointSectionLayout
{
    private const int Size = 80;
    internal const int FileOffset = 0x279;

    [FieldOffset(0)] public ushort Magic;
    [FieldOffset(2)] public uint Version;
    [FieldOffset(6)] public ushort SectionSize;
    [FieldOffset(8)] public WaypointDifficultyLayout Normal;
    [FieldOffset(32)] public WaypointDifficultyLayout Nightmare;
    [FieldOffset(56)] public WaypointDifficultyLayout Hell;

    internal readonly bool IsValid => Magic == WaypointSection.Magic;

    /// <summary>Unlocks all waypoints for all difficulties.</summary>
    public void UnlockAllWaypoints()
    {
        Normal.UnlockAllWaypoints();
        Nightmare.UnlockAllWaypoints();
        Hell.UnlockAllWaypoints();
    }
}

/// <summary>
/// Player intro section layout (52 bytes at offset 0x2C9).
/// Layout: Header(2) + SectionSize(2) + QuestIntro[3×8] + NpcIntro[3×8]
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct PlayerIntroSectionLayout
{
    private const int Size = 52;
    internal const int FileOffset = 0x2C9;

    [FieldOffset(0)] public ushort Header;
    [FieldOffset(2)] public ushort SectionSize;
    [FieldOffset(4)] private fixed byte _questIntroNormal[8];
    [FieldOffset(12)] private fixed byte _questIntroNightmare[8];
    [FieldOffset(20)] private fixed byte _questIntroHell[8];
    [FieldOffset(28)] private fixed byte _npcIntroNormal[8];
    [FieldOffset(36)] private fixed byte _npcIntroNightmare[8];
    [FieldOffset(44)] private fixed byte _npcIntroHell[8];

    public Span<byte> QuestIntroNormal => MemoryMarshal.CreateSpan(ref _questIntroNormal[0], 8);
    public Span<byte> QuestIntroNightmare => MemoryMarshal.CreateSpan(ref _questIntroNightmare[0], 8);
    public Span<byte> QuestIntroHell => MemoryMarshal.CreateSpan(ref _questIntroHell[0], 8);
    public Span<byte> NpcIntroNormal => MemoryMarshal.CreateSpan(ref _npcIntroNormal[0], 8);
    public Span<byte> NpcIntroNightmare => MemoryMarshal.CreateSpan(ref _npcIntroNightmare[0], 8);
    public Span<byte> NpcIntroHell => MemoryMarshal.CreateSpan(ref _npcIntroHell[0], 8);

    internal readonly bool IsValid => Header == PlayerIntroSection.Magic;
}

/// <summary>
/// Blittable layout for the fixed sections of D2 save files (765 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct D2SaveLayout
{
    private const int Size = 765;

    [FieldOffset(HeaderLayout.FileOffset)] public HeaderLayout Header;
    [FieldOffset(CharacterLayout.FileOffset)] public CharacterLayout Character;
    [FieldOffset(QuestSectionLayout.FileOffset)] public QuestSectionLayout Quests;
    [FieldOffset(WaypointSectionLayout.FileOffset)] public WaypointSectionLayout Waypoints;
    [FieldOffset(PlayerIntroSectionLayout.FileOffset)] public PlayerIntroSectionLayout PlayerIntro;

    /// <summary>Validates all section magic values.</summary>
    private readonly bool IsValid =>
        Header.IsValid &&
        Quests.IsValid &&
        Waypoints.IsValid &&
        PlayerIntro.IsValid;

    /// <summary>
    /// Character name (version-aware).
    /// Version 96-97 stores name in Character section, version 98+ stores in Preview section.
    /// </summary>
    public string Name
    {
        get => Header.Version > 97 ? Character.Preview.Name : Character.Name;
        set
        {
            if (Header.Version > 97)
            {
                Character.Preview.Name = value;
                Character.Name = "";
            }
            else
            {
                Character.Name = value;
                Character.Preview.Name = "";
            }
        }
    }

    /// <summary>
    /// Gets a reference to the layout from a byte span.
    /// Throws InvalidDataException if magic values are invalid or version is >= 104.
    /// For v104+ saves, use <see cref="D2SaveLayoutV104.From(Span{byte})"/> instead.
    /// </summary>
    public static ref D2SaveLayout From(Span<byte> data)
    {
        ref var layout = ref MemoryMarshal.AsRef<D2SaveLayout>(data);
        if (!layout.IsValid)
            throw new InvalidDataException("Invalid D2 save file: magic values do not match.");
        if (layout.Header.Version >= 104)
            throw new InvalidDataException("Save version >= 104 uses a different header layout. Use D2SaveLayoutV104.From() instead.");
        return ref layout;
    }

    /// <summary>
    /// Gets a read-only reference to the layout from a read-only byte span.
    /// Throws InvalidDataException if magic values are invalid or version is >= 104.
    /// For v104+ saves, use <see cref="D2SaveLayoutV104.FromReadOnly(ReadOnlySpan{byte})"/> instead.
    /// </summary>
    public static ref readonly D2SaveLayout FromReadOnly(ReadOnlySpan<byte> data)
    {
        ref readonly var layout = ref MemoryMarshal.AsRef<D2SaveLayout>(data);
        if (!layout.IsValid)
            throw new InvalidDataException("Invalid D2 save file: magic values do not match.");
        if (layout.Header.Version >= 104)
            throw new InvalidDataException("Save version >= 104 uses a different header layout. Use D2SaveLayoutV104.FromReadOnly() instead.");
        return ref layout;
    }

    /// <summary>
    /// Updates the checksum in the provided data buffer.
    /// </summary>
    public static void UpdateChecksum(Span<byte> data)
        => ChecksumCalculator.Update(data);
}

/// <summary>
/// Blittable layout for the fixed sections of v>=104 D2 save files (833 bytes).
/// v104+ removes the 16-byte Character.Name field and expands PreviewData from 144 to 228 bytes,
/// shifting all downstream section offsets by +68 bytes.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct D2SaveLayoutV104
{
    private const int Size = 833;

    private const int QuestFileOffset = 0x193;
    private const int WaypointFileOffset = 0x2BD;
    private const int PlayerIntroFileOffset = 0x30D;

    [FieldOffset(HeaderLayout.FileOffset)] public HeaderLayout Header;
    [FieldOffset(CharacterLayoutV104.FileOffset)] public CharacterLayoutV104 Character;
    [FieldOffset(QuestFileOffset)] public QuestSectionLayout Quests;
    [FieldOffset(WaypointFileOffset)] public WaypointSectionLayout Waypoints;
    [FieldOffset(PlayerIntroFileOffset)] public PlayerIntroSectionLayout PlayerIntro;

    /// <summary>Validates all section magic values and version.</summary>
    private readonly bool IsValid =>
        Header.IsValid &&
        Header.Version >= 104 &&
        Quests.IsValid &&
        Waypoints.IsValid &&
        PlayerIntro.IsValid;

    /// <summary>
    /// Character name (always from Preview.Name in v104+, since Character.Name was removed).
    /// </summary>
    public string Name
    {
        get => Character.Preview.Name;
        set => Character.Preview.Name = value;
    }

    /// <summary>
    /// Gets a reference to the layout from a byte span.
    /// Throws InvalidDataException if magic values are invalid or version is &lt; 104.
    /// </summary>
    public static ref D2SaveLayoutV104 From(Span<byte> data)
    {
        ref var layout = ref MemoryMarshal.AsRef<D2SaveLayoutV104>(data);
        if (!layout.IsValid)
            throw new InvalidDataException("Invalid D2 save file: magic values do not match or version < 104.");
        return ref layout;
    }

    /// <summary>
    /// Gets a read-only reference to the layout from a read-only byte span.
    /// Throws InvalidDataException if magic values are invalid or version is &lt; 104.
    /// </summary>
    public static ref readonly D2SaveLayoutV104 FromReadOnly(ReadOnlySpan<byte> data)
    {
        ref readonly var layout = ref MemoryMarshal.AsRef<D2SaveLayoutV104>(data);
        if (!layout.IsValid)
            throw new InvalidDataException("Invalid D2 save file: magic values do not match or version < 104.");
        return ref layout;
    }

    /// <summary>
    /// Updates the checksum in the provided data buffer.
    /// </summary>
    public static void UpdateChecksum(Span<byte> data)
        => ChecksumCalculator.Update(data);
}