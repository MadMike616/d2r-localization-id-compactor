using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Skill assignment (hotkey).
/// </summary>
public partial class Skill
{
    /// <summary>The skill ID (0-32767).</summary>
    public ushort SkillId { get; set; }

    /// <summary>Which side of the skill bar this skill is assigned to.</summary>
    public Side Side { get; set; }

    /// <summary>Index of the item in the item section for item-granted skills.</summary>
    public ushort ItemIndex { get; set; }

    /// <summary>
    /// Reads a skill from a BitReader.
    /// </summary>
    public static Skill Read(ref BitReader reader)
    {
        ushort raw = reader.ReadUInt16();
        return new Skill
        {
            SkillId = (ushort)(raw & 0x7FFF),
            Side = (Side)(raw >> 15),
            ItemIndex = reader.ReadUInt16()
        };
    }

    /// <summary>
    /// Writes a skill to a BitWriter.
    /// </summary>
    public void Write(ref BitWriter writer)
    {
        ushort raw = (ushort)((SkillId & 0x7FFF) | ((int)Side << 15));
        writer.WriteUInt16(raw);
        writer.WriteUInt16(ItemIndex);
    }
}
