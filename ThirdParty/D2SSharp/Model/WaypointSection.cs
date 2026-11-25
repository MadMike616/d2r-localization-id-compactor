using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Waypoint data section (80 bytes).
/// </summary>
public partial class WaypointSection
{
    /// <summary>Section magic: "WS" (0x5357)</summary>
    public const ushort Magic = 0x5357; // "WS" little-endian

    /// <summary>Section size in bytes.</summary>
    public const int Size = 80;

    /// <summary>Section version (always 1).</summary>
    public uint Version { get; set; } = 1;

    /// <summary>Section size (always 80).</summary>
    public ushort SectionSize { get; set; } = Size;

    /// <summary>Waypoint data for Normal difficulty (24 bytes).</summary>
    public WaypointDifficultyData Normal { get; set; } = new();

    /// <summary>Waypoint data for Nightmare difficulty (24 bytes).</summary>
    public WaypointDifficultyData Nightmare { get; set; } = new();

    /// <summary>Waypoint data for Hell difficulty (24 bytes).</summary>
    public WaypointDifficultyData Hell { get; set; } = new();

    /// <summary>
    /// Gets waypoint data for a specific difficulty.
    /// </summary>
    public WaypointDifficultyData GetWaypoints(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Normal => Normal,
        Difficulty.Nightmare => Nightmare,
        Difficulty.Hell => Hell,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
    };

    /// <summary>
    /// Reads a waypoint section from a BitReader.
    /// </summary>
    public static WaypointSection Read(ref BitReader reader)
    {
        var section = new WaypointSection();

        // Verify magic "WS"
        ushort magic = reader.ReadUInt16();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid waypoint section magic: 0x{magic:X4}");

        section.Version = reader.ReadUInt32();
        section.SectionSize = reader.ReadUInt16();

        section.Normal = WaypointDifficultyData.Read(ref reader);
        section.Nightmare = WaypointDifficultyData.Read(ref reader);
        section.Hell = WaypointDifficultyData.Read(ref reader);

        return section;
    }

    /// <summary>
    /// Writes a waypoint section to a BitWriter.
    /// </summary>
    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt16(Magic);

        writer.WriteUInt32(Version);
        writer.WriteUInt16(SectionSize);

        Normal.Write(ref writer);
        Nightmare.Write(ref writer);
        Hell.Write(ref writer);
    }
}

/// <summary>
/// Waypoint data for a single difficulty (24 bytes).
/// Layout: ushort version + 112-bit waypoint bitfield (39 used in vanilla) + 8 bytes reserved.
/// </summary>
public partial class WaypointDifficultyData
{
    /// <summary>Size per difficulty in bytes.</summary>
    public const int Size = 24;

    /// <summary>Waypoint record version. The game writes 0x0102 and resets all waypoints on load if it differs.</summary>
    public ushort Version { get; set; } = 0x0102;

    /// <summary>Act I waypoints (9 waypoints).</summary>
    public ActIWaypoints ActI { get; set; } = ActIWaypoints.RogueEncampment;

    /// <summary>Act II waypoints (9 waypoints).</summary>
    public ActIIWaypoints ActII { get; set; } = ActIIWaypoints.None;

    /// <summary>Act III waypoints (9 waypoints).</summary>
    public ActIIIWaypoints ActIII { get; set; } = ActIIIWaypoints.None;

    /// <summary>Act IV waypoints (3 waypoints).</summary>
    public ActIVWaypoints ActIV { get; set; } = ActIVWaypoints.None;

    /// <summary>Act V waypoints (9 waypoints).</summary>
    public ActVWaypoints ActV { get; set; } = ActVWaypoints.None;

    /// <summary>Waypoint bits 39-47 (9 bits). Part of the game's 112-bit waypoint field, unused in vanilla.</summary>
    public ushort ExtraWaypointBits { get; set; }

    /// <summary>Waypoint bits 48-111 (8 bytes). Part of the game's 112-bit waypoint field, unused in vanilla.</summary>
    public byte[] ExtraWaypointBytes { get; set; } = new byte[8];

    /// <summary>Reserved bytes (8 bytes, always written as zero and never read by the game).</summary>
    public byte[] Reserved { get; set; } = new byte[8];

    /// <summary>
    /// Checks if a specific waypoint is activated by global index (0-38).
    /// </summary>
    public bool IsWaypointActive(int waypointIndex)
    {
        if (waypointIndex < 0 || waypointIndex > 38) return false;

        return waypointIndex switch
        {
            < 9 => ((ushort)ActI & (1 << waypointIndex)) != 0,
            < 18 => ((ushort)ActII & (1 << (waypointIndex - 9))) != 0,
            < 27 => ((ushort)ActIII & (1 << (waypointIndex - 18))) != 0,
            < 30 => ((byte)ActIV & (1 << (waypointIndex - 27))) != 0,
            _ => ((ushort)ActV & (1 << (waypointIndex - 30))) != 0
        };
    }

    /// <summary>
    /// Sets or clears a specific waypoint activation by global index (0-38).
    /// </summary>
    public void SetWaypoint(int waypointIndex, bool active)
    {
        if (waypointIndex < 0 || waypointIndex > 38) return;

        if (waypointIndex < 9)
        {
            var bit = (ushort)(1 << waypointIndex);
            ActI = active ? ActI | (ActIWaypoints)bit : ActI & ~(ActIWaypoints)bit;
        }
        else if (waypointIndex < 18)
        {
            var bit = (ushort)(1 << (waypointIndex - 9));
            ActII = active ? ActII | (ActIIWaypoints)bit : ActII & ~(ActIIWaypoints)bit;
        }
        else if (waypointIndex < 27)
        {
            var bit = (ushort)(1 << (waypointIndex - 18));
            ActIII = active ? ActIII | (ActIIIWaypoints)bit : ActIII & ~(ActIIIWaypoints)bit;
        }
        else if (waypointIndex < 30)
        {
            var bit = (byte)(1 << (waypointIndex - 27));
            ActIV = active ? ActIV | (ActIVWaypoints)bit : ActIV & ~(ActIVWaypoints)bit;
        }
        else
        {
            var bit = (ushort)(1 << (waypointIndex - 30));
            ActV = active ? ActV | (ActVWaypoints)bit : ActV & ~(ActVWaypoints)bit;
        }
    }

    /// <summary>
    /// Reads waypoint difficulty data from a BitReader.
    /// </summary>
    public static WaypointDifficultyData Read(ref BitReader reader)
    {
        var data = new WaypointDifficultyData();

        // Read header (2 bytes)
        data.Version = reader.ReadUInt16();

        // Read waypoint bits: 9 + 9 + 9 + 3 + 9 = 39 bits
        data.ActI = (ActIWaypoints)reader.ReadBits(9);
        data.ActII = (ActIIWaypoints)reader.ReadBits(9);
        data.ActIII = (ActIIIWaypoints)reader.ReadBits(9);
        data.ActIV = (ActIVWaypoints)reader.ReadBits(3);
        data.ActV = (ActVWaypoints)reader.ReadBits(9);

        // Remaining waypoint bits: 9 bits to complete 48 bits (6 bytes), then 64 bits (8 bytes)
        data.ExtraWaypointBits = (ushort)reader.ReadBits(9);
        reader.ReadBytes(data.ExtraWaypointBytes);

        // 8 reserved bytes
        reader.ReadBytes(data.Reserved);

        return data;
    }

    /// <summary>
    /// Writes waypoint difficulty data to a BitWriter.
    /// </summary>
    public void Write(ref BitWriter writer)
    {
        // Write header (2 bytes)
        writer.WriteUInt16(Version);

        // Write waypoint bits: 9 + 9 + 9 + 3 + 9 = 39 bits
        writer.WriteBits((ushort)ActI, 9);
        writer.WriteBits((ushort)ActII, 9);
        writer.WriteBits((ushort)ActIII, 9);
        writer.WriteBits((byte)ActIV, 3);
        writer.WriteBits((ushort)ActV, 9);

        // Remaining waypoint bits: 9 bits to complete 48 bits (6 bytes), then 64 bits (8 bytes)
        writer.WriteBits(ExtraWaypointBits, 9);
        writer.WriteBytes(ExtraWaypointBytes);

        // 8 reserved bytes
        writer.WriteBytes(Reserved);
    }
}
