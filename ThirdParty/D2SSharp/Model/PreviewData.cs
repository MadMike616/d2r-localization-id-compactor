using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Represents the preview data section of a D2S save file.
/// This data is used by the game to display character information in the character selection screen
/// without needing to fully parse the save file.
/// </summary>
/// <remarks>
/// Two layouts exist:
/// v&lt;=103 (144 bytes): ExpansionSaveTime(8) ClassicSaveTime(8) GuildEmblemColor(1) GameVersion(1)
///     Padding(2) ExpansionExperience(4) ClassicExperience(4) PreviewItems×4(48) Name(60) Reserved(8)
/// v>=104 (228 bytes): SaveTimes×6(48) Experiences×6(24) GuildEmblemColor(1) GameVersion(1)
///     Padding(2) PreviewItems×4(48) Name(96) Reserved(8)
/// </remarks>
public partial class PreviewData
{
    /// <summary>
    /// Ladder save timestamps stored as Windows FILETIME, one slot per game version:
    /// [0]=Expansion, [1]=Classic, [2]=Reign of the Warlock. The client only updates these
    /// three, and only in ladder games. Slots [3..5] are loaded and written back unchanged by
    /// the client; they may be maintained server-side (e.g. non-ladder counterparts of
    /// [0..2]), which is unverified. v&lt;=103 stores slots [0] and [1] only.
    /// </summary>
    public ulong[] SaveTimes { get; set; } = new ulong[6];

    /// <summary>
    /// Ladder experience, one slot per game version (same indexing as <see cref="SaveTimes"/>).
    /// Only updated in ladder games. v&lt;=103 stores slots [0] and [1] only.
    /// </summary>
    public uint[] Experiences { get; set; } = new uint[6];

    /// <summary>
    /// Guild emblem background color index. The game only accepts 0 or 0xFF in saves;
    /// 0xFF on a ladder-flagged character lets it load into a non-ladder game.
    /// </summary>
    public byte GuildEmblemColor { get; set; }

    /// <summary>Game version (Classic/Expansion/Reign of the Warlock), serialized as a byte.
    /// For v&lt;=103 the game falls back to the Expansion character flag when this is not 1-3.</summary>
    public GameVersion GameVersion { get; set; }

    /// <summary>Padding after GameVersion (never written by the game, always zero).</summary>
    public ushort PreviewPadding { get; set; }

    /// <summary>Preview data for the item equipped in the left hand slot.</summary>
    public PreviewItem LeftHand { get; set; } = new();

    /// <summary>Preview data for the item equipped in the right hand slot.</summary>
    public PreviewItem RightHand { get; set; } = new();

    /// <summary>Preview data for the item equipped in the torso slot.</summary>
    public PreviewItem Torso { get; set; } = new();

    /// <summary>Preview data for the item equipped in the head slot.</summary>
    public PreviewItem Head { get; set; } = new();

    /// <summary>Character name. v&lt;=103: 60 bytes. v>=104: 96 bytes.</summary>
    public string Name { get; set; } = "";

    /// <summary>Reserved bytes after Name (always written as zero, never read by the game).</summary>
    public ulong Reserved { get; set; }

    // Legacy property accessors for backward compatibility
    /// <summary>Save timestamp for expansion mode (SaveTimes[0]).</summary>
    public ulong ExpansionSaveTime
    {
        get => SaveTimes[0];
        set => SaveTimes[0] = value;
    }

    /// <summary>Save timestamp for classic mode (SaveTimes[1]).</summary>
    public ulong ClassicSaveTime
    {
        get => SaveTimes[1];
        set => SaveTimes[1] = value;
    }

    /// <summary>Total experience points in expansion mode (Experiences[0]).</summary>
    public uint ExpansionExperience
    {
        get => Experiences[0];
        set => Experiences[0] = value;
    }

    /// <summary>Total experience points in classic mode (Experiences[1]).</summary>
    public uint ClassicExperience
    {
        get => Experiences[1];
        set => Experiences[1] = value;
    }

    /// <summary>
    /// Reads a player Preview data section from a BitReader.
    /// </summary>
    public static PreviewData Read(ref BitReader reader, uint saveVersion)
    {
        var data = new PreviewData();

        if (saveVersion >= 104)
        {
            // v>=104: 6 save times (48 bytes)
            for (int i = 0; i < 6; i++)
                data.SaveTimes[i] = reader.ReadUInt64();

            // 6 experiences (24 bytes)
            for (int i = 0; i < 6; i++)
                data.Experiences[i] = reader.ReadUInt32();

            ReadEmblemAndVersion(ref reader, data);
        }
        else
        {
            // v<=103: 2 save times, guild emblem + game version, 2 experiences
            data.SaveTimes[0] = reader.ReadUInt64();
            data.SaveTimes[1] = reader.ReadUInt64();
            ReadEmblemAndVersion(ref reader, data);
            data.Experiences[0] = reader.ReadUInt32();
            data.Experiences[1] = reader.ReadUInt32();
        }

        data.LeftHand = PreviewItem.Read(ref reader);
        data.RightHand = PreviewItem.Read(ref reader);
        data.Torso = PreviewItem.Read(ref reader);
        data.Head = PreviewItem.Read(ref reader);

        int nameSize = saveVersion >= 104 ? 96 : 60;
        data.Name = reader.ReadFixedString(nameSize);
        data.Reserved = reader.ReadUInt64();

        return data;
    }

    private static void ReadEmblemAndVersion(ref BitReader reader, PreviewData data)
    {
        // GuildEmblemColor as byte, GameVersion as byte, Padding as ushort
        data.GuildEmblemColor = reader.ReadByte();
        data.GameVersion = (GameVersion)reader.ReadByte();
        data.PreviewPadding = reader.ReadUInt16();
    }

    private void WriteEmblemAndVersion(ref BitWriter writer)
    {
        writer.WriteByte(GuildEmblemColor);
        writer.WriteByte((byte)GameVersion);
        writer.WriteUInt16(PreviewPadding);
    }

    /// <summary>Writes the player Preview data section to a BitWriter.</summary>
    public void Write(ref BitWriter writer, uint saveVersion)
    {
        if (saveVersion >= 104)
        {
            // v>=104: 6 save times (48 bytes)
            for (int i = 0; i < 6; i++)
                writer.WriteUInt64(SaveTimes[i]);

            // 6 experiences (24 bytes)
            for (int i = 0; i < 6; i++)
                writer.WriteUInt32(Experiences[i]);

            WriteEmblemAndVersion(ref writer);
        }
        else
        {
            // v<=103: 2 save times, guild emblem + game version, 2 experiences
            writer.WriteUInt64(SaveTimes[0]);
            writer.WriteUInt64(SaveTimes[1]);
            WriteEmblemAndVersion(ref writer);
            writer.WriteUInt32(Experiences[0]);
            writer.WriteUInt32(Experiences[1]);
        }

        LeftHand.Write(ref writer);
        RightHand.Write(ref writer);
        Torso.Write(ref writer);
        Head.Write(ref writer);

        int nameSize = saveVersion >= 104 ? 96 : 60;
        writer.WriteFixedString(Name, nameSize);
        writer.WriteUInt64(Reserved);
    }
}
