using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Act I quests (8 slots × 2 bytes = 16 bytes).
/// </summary>
public partial class ActIQuests
{
    public QuestFlags Introduction { get; set; }
    public QuestFlags DenOfEvil { get; set; }
    public QuestFlags SistersBurialGrounds { get; set; }
    public QuestFlags ToolsOfTheTrade { get; set; }
    public QuestFlags TheSearchForCain { get; set; }
    public QuestFlags TheForgottenTower { get; set; }
    public QuestFlags SistersToTheSlaughter { get; set; }
    public QuestFlags Completion { get; set; }

    public static ActIQuests Read(ref BitReader reader) => new()
    {
        Introduction = (QuestFlags)reader.ReadUInt16(),
        DenOfEvil = (QuestFlags)reader.ReadUInt16(),
        SistersBurialGrounds = (QuestFlags)reader.ReadUInt16(),
        ToolsOfTheTrade = (QuestFlags)reader.ReadUInt16(),
        TheSearchForCain = (QuestFlags)reader.ReadUInt16(),
        TheForgottenTower = (QuestFlags)reader.ReadUInt16(),
        SistersToTheSlaughter = (QuestFlags)reader.ReadUInt16(),
        Completion = (QuestFlags)reader.ReadUInt16()
    };

    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt16((ushort)Introduction);
        writer.WriteUInt16((ushort)DenOfEvil);
        writer.WriteUInt16((ushort)SistersBurialGrounds);
        writer.WriteUInt16((ushort)ToolsOfTheTrade);
        writer.WriteUInt16((ushort)TheSearchForCain);
        writer.WriteUInt16((ushort)TheForgottenTower);
        writer.WriteUInt16((ushort)SistersToTheSlaughter);
        writer.WriteUInt16((ushort)Completion);
    }
}

/// <summary>
/// Act II quests (8 slots × 2 bytes = 16 bytes).
/// </summary>
public partial class ActIIQuests
{
    public QuestFlags Introduction { get; set; }
    public QuestFlags RadamentsLair { get; set; }
    public QuestFlags TheHoradricStaff { get; set; }
    public QuestFlags TaintedSun { get; set; }
    public QuestFlags ArcaneSanctuary { get; set; }
    public QuestFlags TheSummoner { get; set; }
    public QuestFlags TheSevenTombs { get; set; }
    public QuestFlags Completion { get; set; }

    public static ActIIQuests Read(ref BitReader reader) => new()
    {
        Introduction = (QuestFlags)reader.ReadUInt16(),
        RadamentsLair = (QuestFlags)reader.ReadUInt16(),
        TheHoradricStaff = (QuestFlags)reader.ReadUInt16(),
        TaintedSun = (QuestFlags)reader.ReadUInt16(),
        ArcaneSanctuary = (QuestFlags)reader.ReadUInt16(),
        TheSummoner = (QuestFlags)reader.ReadUInt16(),
        TheSevenTombs = (QuestFlags)reader.ReadUInt16(),
        Completion = (QuestFlags)reader.ReadUInt16()
    };

    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt16((ushort)Introduction);
        writer.WriteUInt16((ushort)RadamentsLair);
        writer.WriteUInt16((ushort)TheHoradricStaff);
        writer.WriteUInt16((ushort)TaintedSun);
        writer.WriteUInt16((ushort)ArcaneSanctuary);
        writer.WriteUInt16((ushort)TheSummoner);
        writer.WriteUInt16((ushort)TheSevenTombs);
        writer.WriteUInt16((ushort)Completion);
    }
}

/// <summary>
/// Act III quests (8 slots × 2 bytes = 16 bytes).
/// </summary>
public partial class ActIIIQuests
{
    public QuestFlags Introduction { get; set; }
    public QuestFlags LamEsensTome { get; set; }
    public QuestFlags KhalimsWill { get; set; }
    public QuestFlags BladeOfTheOldReligion { get; set; }
    public QuestFlags TheGoldenBird { get; set; }
    public QuestFlags TheBlackenedTemple { get; set; }
    public QuestFlags TheGuardian { get; set; }
    public QuestFlags Completion { get; set; }

    public static ActIIIQuests Read(ref BitReader reader) => new()
    {
        Introduction = (QuestFlags)reader.ReadUInt16(),
        LamEsensTome = (QuestFlags)reader.ReadUInt16(),
        KhalimsWill = (QuestFlags)reader.ReadUInt16(),
        BladeOfTheOldReligion = (QuestFlags)reader.ReadUInt16(),
        TheGoldenBird = (QuestFlags)reader.ReadUInt16(),
        TheBlackenedTemple = (QuestFlags)reader.ReadUInt16(),
        TheGuardian = (QuestFlags)reader.ReadUInt16(),
        Completion = (QuestFlags)reader.ReadUInt16()
    };

    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt16((ushort)Introduction);
        writer.WriteUInt16((ushort)LamEsensTome);
        writer.WriteUInt16((ushort)KhalimsWill);
        writer.WriteUInt16((ushort)BladeOfTheOldReligion);
        writer.WriteUInt16((ushort)TheGoldenBird);
        writer.WriteUInt16((ushort)TheBlackenedTemple);
        writer.WriteUInt16((ushort)TheGuardian);
        writer.WriteUInt16((ushort)Completion);
    }
}

/// <summary>
/// Act IV quests (8 slots × 2 bytes = 16 bytes).
/// Only 4 actual quests; the last 3 slots hold gossip state for NPCs from other acts
/// (quest record slots 29-31).
/// </summary>
public partial class ActIVQuests
{
    public QuestFlags Introduction { get; set; }
    public QuestFlags TheFallenAngel { get; set; }
    public QuestFlags TerrorsEnd { get; set; }
    public QuestFlags Hellforge { get; set; }
    public QuestFlags Completion { get; set; }

    /// <summary>Act I: Flavie's gossip about the Den of Evil (game quest A1Q7). Typically 0.</summary>
    public QuestFlags FlavieGossip { get; set; }

    /// <summary>Act II: guard gossip (game quest A2Q7). Bit 0 set on talk, bit 13 set on specific messages.</summary>
    public QuestFlags GuardGossip1 { get; set; }

    /// <summary>Act II: guard gossip (game quest A2Q8). Bit 0 set on talk.</summary>
    public QuestFlags GuardGossip2 { get; set; }

    public static ActIVQuests Read(ref BitReader reader) => new()
    {
        Introduction = (QuestFlags)reader.ReadUInt16(),
        TheFallenAngel = (QuestFlags)reader.ReadUInt16(),
        TerrorsEnd = (QuestFlags)reader.ReadUInt16(),
        Hellforge = (QuestFlags)reader.ReadUInt16(),
        Completion = (QuestFlags)reader.ReadUInt16(),
        FlavieGossip = (QuestFlags)reader.ReadUInt16(),
        GuardGossip1 = (QuestFlags)reader.ReadUInt16(),
        GuardGossip2 = (QuestFlags)reader.ReadUInt16()
    };

    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt16((ushort)Introduction);
        writer.WriteUInt16((ushort)TheFallenAngel);
        writer.WriteUInt16((ushort)TerrorsEnd);
        writer.WriteUInt16((ushort)Hellforge);
        writer.WriteUInt16((ushort)Completion);
        writer.WriteUInt16((ushort)FlavieGossip);
        writer.WriteUInt16((ushort)GuardGossip1);
        writer.WriteUInt16((ushort)GuardGossip2);
    }
}

/// <summary>
/// Act V quests (16 slots × 2 bytes = 32 bytes, quest record slots 32-47).
/// The game only processes slots 0-41; slots 42-47 are never used.
/// </summary>
public partial class ActVQuests
{
    /// <summary>Act III: Dark Wanderer event (game quest A3Q7). Bit 0 set once seen.</summary>
    public QuestFlags DarkWanderer { get; set; }

    /// <summary>Act IV: Hadriel's gossip (game quest A4Q4). Typically 0.</summary>
    public QuestFlags HadrielGossip { get; set; }

    /// <summary>Unused slot (not referenced by the game).</summary>
    public QuestFlags Unused34 { get; set; }
    public QuestFlags SiegeOnHarrogath { get; set; }
    public QuestFlags RescueOnMountArreat { get; set; }
    public QuestFlags PrisonOfIce { get; set; }
    public QuestFlags BetrayalOfHarrogath { get; set; }
    public QuestFlags RiteOfPassage { get; set; }
    public QuestFlags EveOfDestruction { get; set; }

    /// <summary>
    /// Akara's stat/skill reset (slot 41). <see cref="QuestFlags.RewardGranted"/> = reset used,
    /// <see cref="QuestFlags.RewardPending"/> = reset available.
    /// </summary>
    public QuestFlags AkaraRespec { get; set; }

    public QuestFlags Unused42 { get; set; }
    public QuestFlags Unused43 { get; set; }
    public QuestFlags Unused44 { get; set; }
    public QuestFlags Unused45 { get; set; }
    public QuestFlags Unused46 { get; set; }
    public QuestFlags Unused47 { get; set; }

    public static ActVQuests Read(ref BitReader reader) => new()
    {
        DarkWanderer = (QuestFlags)reader.ReadUInt16(),
        HadrielGossip = (QuestFlags)reader.ReadUInt16(),
        Unused34 = (QuestFlags)reader.ReadUInt16(),
        SiegeOnHarrogath = (QuestFlags)reader.ReadUInt16(),
        RescueOnMountArreat = (QuestFlags)reader.ReadUInt16(),
        PrisonOfIce = (QuestFlags)reader.ReadUInt16(),
        BetrayalOfHarrogath = (QuestFlags)reader.ReadUInt16(),
        RiteOfPassage = (QuestFlags)reader.ReadUInt16(),
        EveOfDestruction = (QuestFlags)reader.ReadUInt16(),
        AkaraRespec = (QuestFlags)reader.ReadUInt16(),
        Unused42 = (QuestFlags)reader.ReadUInt16(),
        Unused43 = (QuestFlags)reader.ReadUInt16(),
        Unused44 = (QuestFlags)reader.ReadUInt16(),
        Unused45 = (QuestFlags)reader.ReadUInt16(),
        Unused46 = (QuestFlags)reader.ReadUInt16(),
        Unused47 = (QuestFlags)reader.ReadUInt16()
    };

    public void Write(ref BitWriter writer)
    {
        writer.WriteUInt16((ushort)DarkWanderer);
        writer.WriteUInt16((ushort)HadrielGossip);
        writer.WriteUInt16((ushort)Unused34);
        writer.WriteUInt16((ushort)SiegeOnHarrogath);
        writer.WriteUInt16((ushort)RescueOnMountArreat);
        writer.WriteUInt16((ushort)PrisonOfIce);
        writer.WriteUInt16((ushort)BetrayalOfHarrogath);
        writer.WriteUInt16((ushort)RiteOfPassage);
        writer.WriteUInt16((ushort)EveOfDestruction);
        writer.WriteUInt16((ushort)AkaraRespec);
        writer.WriteUInt16((ushort)Unused42);
        writer.WriteUInt16((ushort)Unused43);
        writer.WriteUInt16((ushort)Unused44);
        writer.WriteUInt16((ushort)Unused45);
        writer.WriteUInt16((ushort)Unused46);
        writer.WriteUInt16((ushort)Unused47);
    }
}

/// <summary>
/// Quest data for a single difficulty (96 bytes).
/// </summary>
public partial class QuestsDifficulty
{
    public ActIQuests ActI { get; set; } = new();
    public ActIIQuests ActII { get; set; } = new();
    public ActIIIQuests ActIII { get; set; } = new();
    public ActIVQuests ActIV { get; set; } = new();
    public ActVQuests ActV { get; set; } = new();

    public static QuestsDifficulty Read(ref BitReader reader) => new()
    {
        ActI = ActIQuests.Read(ref reader),
        ActII = ActIIQuests.Read(ref reader),
        ActIII = ActIIIQuests.Read(ref reader),
        ActIV = ActIVQuests.Read(ref reader),
        ActV = ActVQuests.Read(ref reader)
    };

    public void Write(ref BitWriter writer)
    {
        ActI.Write(ref writer);
        ActII.Write(ref writer);
        ActIII.Write(ref writer);
        ActIV.Write(ref writer);
        ActV.Write(ref writer);
    }
}
