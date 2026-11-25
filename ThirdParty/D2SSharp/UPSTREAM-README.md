# D2SSharp

[![Build](https://github.com/ResurrectedTrader/D2SSharp/actions/workflows/publish-nuget.yml/badge.svg?branch=main)](https://github.com/ResurrectedTrader/D2SSharp/actions/workflows/publish-nuget.yml)
[![NuGet](https://img.shields.io/nuget/v/D2SSharp.svg)](https://www.nuget.org/packages/D2SSharp)

A C# library for reading and writing Diablo 2 save files (.d2s character saves and .d2i shared stash files). Supports both the original Diablo 2: Lord of Destruction format (1.10+) and Diablo 2 Resurrected.

## Features

- Full read/write support for character save files (.d2s) and shared stash (.d2i)
- Supports D2 LOD (version 96) and D2R (version 97-105) formats
- Version conversion across all format boundaries (v96↔v97+, v<=103↔v104+, v<=104↔v105+)
- Complete item parsing including stats, sockets, runewords, and set bonuses
- Shared stash tab types: Normal, AdvancedStash (stackable items), and Chronicle (item find tracking)
- DemonSection support (v103+) for summoned creature persistence
- Full round-tripping support - produces identical outputs, as verified by tests
- Separate [zero-copy overlay API](#overlay-api-zero-copy-access) for modifying header fields (name, level, flags, waypoints) without parsing the full save
- External .txt file support for modded game data
- Zero external dependencies beyond .NET

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
  - [Reading a Character Save](#reading-a-character-save)
  - [Modifying and Saving](#modifying-and-saving)
  - [Reading Shared Stash](#reading-shared-stash)
- [Overlay API (Zero-Copy Access)](#overlay-api-zero-copy-access)
  - [Performance](#performance)
  - [Overlay Limitations](#overlay-limitations)
- [Save Format Versions](#save-format-versions)
- [Version Conversion](#version-conversion)
- [External Data](#external-data)
- [Mod Compatibility](#mod-compatibility)
- [Building](#building)
- [Acknowledgments](#acknowledgments)

## Installation

```bash
dotnet add package D2SSharp
```

Or clone and build from source:

```bash
git clone https://github.com/ResurrectedTrader/D2SSharp.git
cd D2SSharp
dotnet build
```

## Usage

### Reading a Character Save

```csharp
using D2SSharp.Enums;
using D2SSharp.Model;

// Read save file (uses built-in game data)
byte[] saveBytes = File.ReadAllBytes("MyCharacter.d2s");
D2Save save = D2Save.Read(saveBytes);

// Access character info
Console.WriteLine($"Character: {save.Character.Preview.Name}");
Console.WriteLine($"Level: {save.Character.Level}");
Console.WriteLine($"Class: {save.Character.Class}");

// Access player stats
Console.WriteLine($"Strength: {save.Stats.GetStat(StatId.Strength)}");
Console.WriteLine($"Stash Gold: {save.Stats.GetStat(StatId.StashGold)}");

// Iterate items
foreach (var item in save.Items)
{
    Console.WriteLine($"Item: {item.ItemCodeString} (Quality: {item.Quality})");
}
```

### Modifying and Saving

```csharp
// Modify player stats
save.Stats.SetStat(StatId.Strength, 200);
save.Stats.SetStat(StatId.StashGold, 2500000);

// Modify character fields directly
save.Character.Level = 99;

// Write back to file
File.WriteAllBytes("MyCharacter.d2s", save.ToBytes());

// Or write to a pre-allocated buffer for zero-copy scenarios
byte[] buffer = new byte[save.EstimateSize()];
int bytesWritten = save.Write(buffer);
File.WriteAllBytes("MyCharacter.d2s", buffer.AsSpan(0, bytesWritten).ToArray());
```

### Working with Items

```csharp
// Access item stats
foreach (var item in save.Items)
{
    // Item properties
    Console.WriteLine($"{item.ItemCodeString}: Level {item.ItemLevel}, Quality {item.Quality}");

    // Item stats (complete items only)
    foreach (var stat in item.Stats)
    {
        Console.WriteLine($"  {stat.Id} = {stat.Value}");
    }

    // Stats are mutable
    var fireStat = item.Stats.FirstOrDefault(s => s.Id == StatId.FireResist);
    if (fireStat != null)
        fireStat.Value = 50;

    // Add new stats
    item.Stats.Add(new Stat { Id = StatId.MagicFind, Value = 25 });

    // Socketed items
    foreach (var socket in item.Sockets)
    {
        Console.WriteLine($"  Socket: {socket?.ItemCodeString}");
    }
}
```

### Working with Skills

```csharp
// Skills are indexed 0-29 within each class
// Get/set skill levels by index
save.Skills[0] = 20;  // Max out first skill

// Map between skill index and actual skill ID
int skillId = save.Skills.GetSkillId(0);     // Index -> skill ID
int index = save.Skills.GetSkillIndex(skillId); // Skill ID -> index
```

### Reading Shared Stash

```csharp
byte[] stashBytes = File.ReadAllBytes("SharedStashSoftCoreV2.d2i");
D2StashSave stash = D2StashSave.Read(stashBytes);

foreach (var tab in stash)
{
    Console.WriteLine($"Tab type: {tab.TabType}, Gold: {tab.Gold}");

    if (tab.TabType == StashTabType.Chronicle)
    {
        // Chronicle tabs track found set/unique/runeword items
        Console.WriteLine($"  Chronicle entries: {tab.Chronicle!.SetEntries.Count}");
    }
    else
    {
        // Normal and AdvancedStash tabs contain items
        Console.WriteLine($"  Items: {tab.Items.Count}");
    }
}
```

## Overlay API (Zero-Copy Access)

For simple modifications to the fixed-size header sections, the overlay API provides direct memory access without parsing the entire save file. Two layout structs exist because v104+ saves have a different header layout:

- **`D2SaveLayout`** (765 bytes) — for saves with version <= 103
- **`D2SaveLayoutV104`** (833 bytes) — for saves with version >= 104

Using the wrong layout for a version throws `InvalidDataException`.

```csharp
using D2SSharp.Model;

byte[] data = File.ReadAllBytes("MyCharacter.d2s");

// Check version to pick the right layout
uint version = BitConverter.ToUInt32(data, 4);
if (version >= 104)
{
    ref var overlay = ref D2SaveLayoutV104.From(data);

    // Name always lives in Preview for v104+
    Console.WriteLine($"Name: {overlay.Name}");
    Console.WriteLine($"Level: {overlay.Character.Level}");
    Console.WriteLine($"GameVersion: {overlay.Character.Preview.GameVersion}");

    // v104+ exposes 6 save time slots and 6 experience slots
    Console.WriteLine($"SaveTimes[0]: {overlay.Character.Preview.SaveTimes[0]}");
    Console.WriteLine($"Experiences[0]: {overlay.Character.Preview.Experiences[0]}");

    overlay.Name = "NewName";
    overlay.Waypoints.UnlockAllWaypoints();
    D2SaveLayoutV104.UpdateChecksum(data);
}
else
{
    ref var overlay = ref D2SaveLayout.From(data);

    // Name is version-aware (Character.Name for v96, Preview.Name for v97+)
    Console.WriteLine($"Name: {overlay.Name}");
    Console.WriteLine($"Level: {overlay.Character.Level}");

    overlay.Name = "NewName";
    overlay.Waypoints.UnlockAllWaypoints();
    D2SaveLayout.UpdateChecksum(data);
}

File.WriteAllBytes("MyCharacter.d2s", data);
```

### Performance

Benchmarks comparing full parsing vs overlay access (tested on a level 99 character with full inventory):

| Operation | Time | Allocated |
|-----------|------|-----------|
| Full Deserialize | 56.1 μs | 225.8 KB |
| Full Serialize | 28.4 μs | 122.5 KB |
| Full Round-Trip | 86.5 μs | 348.3 KB |
| **Overlay: Read Name** | **9.8 ns** | **32 B** |
| **Overlay: Modify + Checksum** | **991 ns** | **2.3 KB** |

The overlay API is **~5700x faster** for reading character name and **~87x faster** for modifying fields and updating the checksum compared to a full round-trip.

### Overlay Limitations

The overlay API only covers the fixed-size header sections (765 bytes for v<=103, 833 bytes for v>=104):

| Section | Supported Fields |
|---------|-----------------|
| Header | Version, FileSize, Checksum |
| Character | Name, Level, Class, Flags, MercData, Hotkeys, Appearance |
| Preview | PreviewItems, SaveTimes, Experiences, GameVersion (v104+) |
| Quests | All quest flags for all difficulties |
| Waypoints | All waypoint flags for all difficulties |
| PlayerIntro | NPC/Quest intro flags |

**Not accessible via overlay**: Player stats (strength, vitality, gold, etc.), skills, items, corpses, mercenary items, iron golem. These require full parsing with `D2Save.Read()`.

## Save Format Versions

| Version | Game | Notes |
|---------|------|-------|
| 96 | D2 LOD 1.10+ | 32-bit item codes, 7-bit strings |
| 97 | D2R 2.0 | Huffman-encoded item codes, 7-bit strings |
| 98-99 | D2R 2.8 | Huffman-encoded item codes, 8-bit strings |
| 100-102 | D2R 3.0 | Advanced stash tab types, chronicle data, item find tracking |
| 103 | D2R 3.0 | DemonSection ("lf" magic) for summoned creature persistence |
| 104 | D2R 3.0 | New header layout (833 bytes), Name moved to PreviewData, GameVersion field |
| 105 | D2R 3.0 | Item quantity uses 1-bit presence flag for all items |

## Version Conversion

The library supports converting saves between formats by specifying a target version when writing. Conversion is handled across three boundaries: v96↔v97+ (1.14↔D2R), v<=103↔v104+ (old↔new header), and v<=104↔v105+ (item quantity format).

```csharp
// Read a 1.14 save (version 96)
var save = D2Save.Read(File.ReadAllBytes("old_character.d2s"));

// Write as latest D2R format (version 105)
File.WriteAllBytes("new_character.d2s", save.ToBytes(targetVersion: 105));
```

### Conversion Details

The library handles the following format differences automatically:

#### 1.14 → D2R

| Field | Conversion |
|-------|------------|
| `Character.Name` | Moved to `Character.Preview.Name` (D2R uses UTF-8 preview name) |
| `Character.Preview.*` | Populated from equipped items (Head, Torso, LeftHand, RightHand) |
| `Character.Preview.Transform` | Looked up from `Character.AppearanceTints` |
| `Character.Preview.FileIndex` | Extracted from item quality data |
| `Character.Preview.Flags` | Set to `Targeting` for primary weapon, 0 for others |
| `Item.Position.BodyLocation` | Zeroed for non-equipped items (D2R requires this) |

#### D2R → 1.14

| Field | Conversion |
|-------|------------|
| `Character.Preview.Name` | Moved to `Character.Name` |
| `Character.Preview.*` | Zeroed (1.14 doesn't use preview items) |
| `Item.Position.BodyLocation` | Kept as `None` for stored items (D2R doesn't preserve original equip slot) |

#### v<=103 ↔ v104+ (Header Layout)

| Field | Conversion |
|-------|------------|
| `Character.Name` | Removed in v104+; name is only in `Character.Preview.Name` |
| `PreviewData` | Expands from 144 to 228 bytes: `SaveTimes[6]`, `Experiences[6]`, `GameVersion` |
| `DemonSection` | Added in v103+; initialized empty when upgrading from earlier versions |

#### v<=104 ↔ v105+ (Item Format)

| Field | Conversion |
|-------|------------|
| `Item.Quantity` | v105+ uses a 1-bit presence flag for all items, not just stackable |

#### Binary Differences

When comparing converted saves to saves created by the game:

| Section | Reason |
|---------|--------|
| Items | Item ordering may differ between saves of the same character |
| Header (Checksum) | Differs due to item ordering differences |

These differences do not affect gameplay - the converted saves are fully functional.

## External Data

The library includes embedded game data tables for versions 96, 97, 99, and 105, which are used automatically. For modded games with custom items/stats, you can provide your own txt files:

```csharp
using D2SSharp.Data;

// Load from a directory containing version subdirectories
// Directory structure:
//   MyTxtFiles/
//     96/
//       armor.txt, itemstatcost.txt, itemtypes.txt, misc.txt, weapons.txt
//     99/
//       armor.txt, itemstatcost.txt, itemtypes.txt, misc.txt, weapons.txt
var modData = new TxtFileExternalData(@"C:\path\to\MyTxtFiles");

// Or load from a single directory for a specific version
var modData = new TxtFileExternalData(@"C:\path\to\MyTxtFiles\99", version: 99);

// Then use it for reading/writing
var save = D2Save.Read(File.ReadAllBytes("modded.d2s"), modData);
```

The library selects version data based on exact match with the save file's version. If the required version is not available, an exception is thrown listing the available versions.

## Mod Compatibility

The library provides limited support for modded save files:

### Supported

- **Custom item data**: See [External Data](#external-data) section for loading mod-specific .txt files
- **Trailing data**: Any bytes after the standard sections are preserved in `D2Save.TrailingData` and written back during round-trip

### Not Supported

- **Missing sections**: Save files must contain all required sections. Files with missing or malformed sections (e.g., Expansion flag set but no MercItems/IronGolem sections) will fail to parse
- **Modified section formats**: The library expects standard D2/D2R section layouts

## Building

```bash
dotnet build D2SSharp.sln
dotnet test
```

## Acknowledgments

This project was vibe coded with [Claude](https://claude.ai).

Special thanks to:
- [D2MOO](https://github.com/ThePhrozenKeep/D2MOO)
- dschu012 for [d2s](https://github.com/dschu012/d2s)
- Killshot
- BRE

## License

MIT
