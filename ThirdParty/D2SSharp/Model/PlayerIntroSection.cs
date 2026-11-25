using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Player introduction/dialog flags section (52 bytes).
/// Tracks which NPCs and quests have been introduced to the player.
/// </summary>
public partial class PlayerIntroSection
{
    /// <summary>Section magic: 0x7701 ("w.")</summary>
    public const ushort Magic = 0x7701;

    /// <summary>Section size in bytes.</summary>
    public const int Size = 52;

    /// <summary>Section header (should be 0x7701).</summary>
    public ushort Header { get; set; } = Magic;

    /// <summary>Section size (should be 52).</summary>
    public ushort SectionSize { get; set; } = Size;

    /// <summary>NPC dialog flags for Normal difficulty.</summary>
    public NpcDialogDifficulty Normal { get; set; } = new();

    /// <summary>NPC dialog flags for Nightmare difficulty.</summary>
    public NpcDialogDifficulty Nightmare { get; set; } = new();

    /// <summary>NPC dialog flags for Hell difficulty.</summary>
    public NpcDialogDifficulty Hell { get; set; } = new();

    /// <summary>
    /// Gets NPC dialog data for a specific difficulty.
    /// </summary>
    public NpcDialogDifficulty GetDialogs(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Normal => Normal,
        Difficulty.Nightmare => Nightmare,
        Difficulty.Hell => Hell,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
    };

    /// <summary>
    /// Reads a player intro section from a BitReader.
    /// </summary>
    public static PlayerIntroSection Read(ref BitReader reader)
    {
        var section = new PlayerIntroSection();

        section.Header = reader.ReadUInt16();

        if (section.Header != Magic)
            throw new InvalidDataException($"Failed to read player intro section, invalid magic: {section.Header}");

        section.SectionSize = reader.ReadUInt16();
        if (section.SectionSize != Size)
            throw new InvalidDataException($"Failed to read player intro section, invalid size: {section.SectionSize}");

        // Quest intro flags for all difficulties first
        reader.ReadBytes(section.Normal.QuestIntroFlags);
        reader.ReadBytes(section.Nightmare.QuestIntroFlags);
        reader.ReadBytes(section.Hell.QuestIntroFlags);

        // Then NPC intro flags for all difficulties
        reader.ReadBytes(section.Normal.NpcIntroFlags);
        reader.ReadBytes(section.Nightmare.NpcIntroFlags);
        reader.ReadBytes(section.Hell.NpcIntroFlags);

        return section;
    }

    /// <summary>
    /// Writes a player intro section to a BitWriter.
    /// </summary>
    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt16(Header);
        writer.WriteUInt16(SectionSize);

        // Quest intro flags for all difficulties first
        writer.WriteBytes(Normal.QuestIntroFlags);
        writer.WriteBytes(Nightmare.QuestIntroFlags);
        writer.WriteBytes(Hell.QuestIntroFlags);

        // Then NPC intro flags for all difficulties
        writer.WriteBytes(Normal.NpcIntroFlags);
        writer.WriteBytes(Nightmare.NpcIntroFlags);
        writer.WriteBytes(Hell.NpcIntroFlags);
    }
}
