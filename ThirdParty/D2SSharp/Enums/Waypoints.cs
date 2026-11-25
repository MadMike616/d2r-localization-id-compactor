namespace D2SSharp.Enums;

/// <summary>
/// Act I waypoints (9 waypoints, bits 0-8).
/// </summary>
[Flags]
public enum ActIWaypoints : ushort
{
    None = 0,
    RogueEncampment = 0x001,   // bit 0 (town)
    ColdPlains = 0x002,         // bit 1
    StonyField = 0x004,         // bit 2
    DarkWood = 0x008,           // bit 3
    BlackMarsh = 0x010,         // bit 4
    OuterCloister = 0x020,      // bit 5
    JailLevel1 = 0x040,         // bit 6
    InnerCloister = 0x080,      // bit 7
    CatacombsLevel2 = 0x100,    // bit 8
    All = 0x1FF
}

/// <summary>
/// Act II waypoints (9 waypoints, bits 9-17).
/// </summary>
[Flags]
public enum ActIIWaypoints : ushort
{
    None = 0,
    LutGholein = 0x001,             // bit 0 (town)
    SewersLevel2 = 0x002,           // bit 1
    DryHills = 0x004,               // bit 2
    HallsOfTheDeadLevel2 = 0x008,   // bit 3
    FarOasis = 0x010,               // bit 4
    LostCity = 0x020,               // bit 5
    PalaceCellarLevel1 = 0x040,     // bit 6
    ArcaneSanctuary = 0x080,        // bit 7
    CanyonOfTheMagi = 0x100,        // bit 8
    All = 0x1FF
}

/// <summary>
/// Act III waypoints (9 waypoints, bits 18-26).
/// </summary>
[Flags]
public enum ActIIIWaypoints : ushort
{
    None = 0,
    KurastDocks = 0x001,            // bit 0 (town)
    SpiderForest = 0x002,           // bit 1
    GreatMarsh = 0x004,             // bit 2
    FlayerJungle = 0x008,           // bit 3
    LowerKurast = 0x010,            // bit 4
    KurastBazaar = 0x020,           // bit 5
    UpperKurast = 0x040,            // bit 6
    Travincal = 0x080,              // bit 7
    DuranceOfHateLevel2 = 0x100,    // bit 8
    All = 0x1FF
}

/// <summary>
/// Act IV waypoints (3 waypoints, bits 27-29).
/// </summary>
[Flags]
public enum ActIVWaypoints : byte
{
    None = 0,
    ThePandemoniumFortress = 0x1,   // bit 0 (town)
    CityOfTheDamned = 0x2,          // bit 1
    RiverOfFlame = 0x4,             // bit 2
    All = 0x7
}

/// <summary>
/// Act V waypoints (9 waypoints, bits 30-38).
/// </summary>
[Flags]
public enum ActVWaypoints : ushort
{
    None = 0,
    Harrogath = 0x001,              // bit 0 (town)
    FrigidHighlands = 0x002,        // bit 1
    ArreatPlateau = 0x004,          // bit 2
    CrystallinePassage = 0x008,     // bit 3
    HallsOfPain = 0x010,            // bit 4
    GlacialTrail = 0x020,           // bit 5
    FrozenTundra = 0x040,           // bit 6
    TheAncientsWay = 0x080,         // bit 7
    WorldstoneKeepLevel2 = 0x100,   // bit 8
    All = 0x1FF
}
