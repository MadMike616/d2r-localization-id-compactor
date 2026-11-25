using System.Reflection;
using D2SSharp.Enums;

namespace D2SSharp.Data;

/// <summary>
/// External data provider that loads from original Diablo 2 .txt data files.
/// Parses itemstatcost.txt, itemtypes.txt, weapons.txt, armor.txt, and misc.txt.
/// Can load from embedded resources (multi-version) or a filesystem directory (multi-version).
/// </summary>
public class TxtFileExternalData : IExternalData
{
    private const string ResourcePrefix = "D2SSharp.Data.Txt.";

    // Embedded versions with their resource prefixes (folder names become _NN due to leading digit)
    private static readonly (uint Version, string Prefix, string[] FileNames)[] EmbeddedVersions =
    [
        (96, "_96.", ["armor.txt", "ItemStatCost.txt", "ItemTypes.txt", "misc.txt", "weapons.txt"]),
        (97, "_97.", ["Armor.txt", "ItemStatCost.txt", "itemtypes.txt", "Misc.txt", "Weapons.txt"]),
        (99, "_99.", ["armor.txt", "itemstatcost.txt", "itemtypes.txt", "misc.txt", "weapons.txt"]),
        (105, "_105.", ["armor.txt", "itemstatcost.txt", "itemtypes.txt", "misc.txt", "weapons.txt"]),
    ];

    /// <summary>
    /// Default instance using embedded txt files for all supported versions.
    /// This instance is lazily initialized and shared across all callers.
    /// </summary>
    public static TxtFileExternalData Default => field ??= new TxtFileExternalData();

    private readonly Dictionary<uint, VersionData> _versionData = new();

    /// <summary>
    /// Creates a new instance using embedded txt files for all supported versions.
    /// </summary>
    public TxtFileExternalData()
    {
        // Load all embedded versions
        foreach (var (version, prefix, fileNames) in EmbeddedVersions)
        {
            var data = LoadVersionDataFromEmbedded(prefix, fileNames);
            _versionData[version] = data;
        }
    }

    /// <summary>
    /// Creates a new instance loading txt files from the specified directory.
    /// The directory should contain subdirectories named with version numbers (e.g., 96, 97, 99),
    /// each containing: itemtypes.txt, itemstatcost.txt, weapons.txt, armor.txt, misc.txt
    /// </summary>
    /// <param name="directory">Path to the directory containing version subdirectories.</param>
    public TxtFileExternalData(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory path cannot be null or empty.", nameof(directory));

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        // Look for version-numbered subdirectories
        var subdirs = Directory.GetDirectories(directory);
        foreach (var subdir in subdirs)
        {
            var dirName = Path.GetFileName(subdir);
            if (uint.TryParse(dirName, out var version))
            {
                var data = LoadVersionDataFromDirectory(subdir);
                _versionData[version] = data;
            }
        }

        if (_versionData.Count == 0)
            throw new InvalidOperationException($"No version subdirectories found in: {directory}. Expected subdirectories named with version numbers (e.g., 96, 97, 99).");
    }

    /// <summary>
    /// Creates a new instance loading txt files from the specified directory for a specific version.
    /// The directory should directly contain the txt files (itemtypes.txt, itemstatcost.txt, etc.),
    /// not version subdirectories.
    /// </summary>
    /// <param name="directory">Path to the directory containing txt files.</param>
    /// <param name="version">The save version these txt files correspond to.</param>
    public TxtFileExternalData(string directory, uint version)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory path cannot be null or empty.", nameof(directory));

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var data = LoadVersionDataFromDirectory(directory);
        _versionData[version] = data;
    }

    private VersionData LoadVersionDataFromEmbedded(string resourcePrefix, string[] fileNames)
    {
        // Map file names to their standard types (case-insensitive comparison)
        string? armorFile = null, miscFile = null, weaponsFile = null, itemStatCostFile = null, itemTypesFile = null;

        foreach (var fileName in fileNames)
        {
            var lower = fileName.ToLowerInvariant();
            if (lower.Contains("armor")) armorFile = fileName;
            else if (lower.Contains("misc")) miscFile = fileName;
            else if (lower.Contains("weapon")) weaponsFile = fileName;
            else if (lower.Contains("itemstatcost")) itemStatCostFile = fileName;
            else if (lower.Contains("itemtype")) itemTypesFile = fileName;
        }

        // Load item types first (needed for type resolution)
        var itemTypes = LoadItemTypesFromEmbedded(resourcePrefix, itemTypesFile!);

        // Load item stat cost
        var itemStatCost = LoadItemStatCostFromEmbedded(resourcePrefix, itemStatCostFile!);

        // Load items
        var weapons = LoadItemsFromEmbedded(resourcePrefix, weaponsFile!, itemTypes);
        var armor = LoadItemsFromEmbedded(resourcePrefix, armorFile!, itemTypes);
        var misc = LoadItemsFromEmbedded(resourcePrefix, miscFile!, itemTypes);

        return BuildVersionData(itemStatCost, itemTypes, weapons, armor, misc);
    }

    private VersionData LoadVersionDataFromDirectory(string directory)
    {
        // Load item types first (needed for type resolution)
        var itemTypes = LoadItemTypesFromDirectory(directory);

        // Load item stat cost
        var itemStatCost = LoadItemStatCostFromDirectory(directory);

        // Load items
        var weapons = LoadItemsFromDirectory(directory, "weapons.txt", itemTypes);
        var armor = LoadItemsFromDirectory(directory, "armor.txt", itemTypes);
        var misc = LoadItemsFromDirectory(directory, "misc.txt", itemTypes);

        return BuildVersionData(itemStatCost, itemTypes, weapons, armor, misc);
    }

    private static VersionData BuildVersionData(ItemStatCostEntry[] itemStatCost, ItemTypeEntry[] itemTypes,
        ItemEntry[] weapons, ItemEntry[] armor, ItemEntry[] misc)
    {
        var items = new ItemEntry[weapons.Length + armor.Length + misc.Length];
        weapons.CopyTo(items, 0);
        armor.CopyTo(items, weapons.Length);
        misc.CopyTo(items, weapons.Length + armor.Length);

        return new VersionData
        {
            ItemStatCost = itemStatCost,
            ItemTypes = itemTypes,
            Items = items,
            ItemCodeToIndex = BuildItemCodeIndex(items),
            TypeDescendants = BuildTypeDescendants(itemTypes)
        };
    }

    private static Dictionary<uint, int> BuildItemCodeIndex(ItemEntry[] items)
    {
        var index = new Dictionary<uint, int>();
        for (int i = 0; i < items.Length; i++)
        {
            var code = items[i].Code;
            if (code != 0)
                index.TryAdd(code, i);
        }
        return index;
    }

    private static string LoadTxtFileFromEmbedded(string resourcePrefix, string fileName)
    {
        var resourceName = ResourcePrefix + resourcePrefix + fileName;
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadTxtFileFromDirectory(string directory, string fileName)
    {
        // Try case-insensitive file matching
        var files = Directory.GetFiles(directory, "*.txt");
        var match = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new FileNotFoundException($"Txt file not found: {Path.Combine(directory, fileName)}", fileName);

        return File.ReadAllText(match);
    }

    private static Dictionary<string, int> ParseHeader(string headerLine)
    {
        var columns = headerLine.Split('\t');
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Length; i++)
        {
            var name = columns[i].Trim();
            if (!string.IsNullOrEmpty(name))
                result.TryAdd(name, i);
        }
        return result;
    }

    private static string GetColumn(string[] columns, int index)
    {
        if (index < 0 || index >= columns.Length)
            return string.Empty;
        return columns[index].Trim();
    }

    private static int ParseInt(string value, int defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private static byte ParseByte(string value, byte defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return byte.TryParse(value, out var result) ? result : defaultValue;
    }

    private static bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static ItemStatCostEntry[] LoadItemStatCostFromEmbedded(string resourcePrefix, string fileName)
    {
        var content = LoadTxtFileFromEmbedded(resourcePrefix, fileName);
        return ParseItemStatCost(content);
    }

    private static ItemStatCostEntry[] LoadItemStatCostFromDirectory(string directory)
    {
        var content = LoadTxtFileFromDirectory(directory, "itemstatcost.txt");
        return ParseItemStatCost(content);
    }

    private static ItemStatCostEntry[] ParseItemStatCost(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return [];

        var header = ParseHeader(lines[0]);
        var result = new List<ItemStatCostEntry>();

        // Get column indices
        int colCsvSigned = header.GetValueOrDefault("CSvSigned", -1);
        int colCsvBits = header.GetValueOrDefault("CSvBits", -1);
        int colCsvParam = header.GetValueOrDefault("CSvParam", -1);
        int colSaveBits = header.GetValueOrDefault("Save Bits", -1);
        int colSaveParamBits = header.GetValueOrDefault("Save Param Bits", -1);
        int colSaveAdd = header.GetValueOrDefault("Save Add", -1);
        int colValShift = header.GetValueOrDefault("ValShift", -1);

        // Use row index (0-based, excluding header) as the stat ID
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                // Empty line still counts as a row - add placeholder entry
                result.Add(new ItemStatCostEntry());
                continue;
            }

            var columns = line.Split('\t');

            result.Add(new ItemStatCostEntry
            {
                CsvSigned = ParseBool(GetColumn(columns, colCsvSigned)),
                CsvBits = ParseByte(GetColumn(columns, colCsvBits)),
                CsvParam = ParseByte(GetColumn(columns, colCsvParam)),
                SaveBits = ParseByte(GetColumn(columns, colSaveBits)),
                SaveParamBits = ParseInt(GetColumn(columns, colSaveParamBits)),
                SaveAdd = ParseInt(GetColumn(columns, colSaveAdd)),
                ValShift = ParseByte(GetColumn(columns, colValShift))
            });
        }

        return [.. result];
    }

    private static ItemTypeEntry[] LoadItemTypesFromEmbedded(string resourcePrefix, string fileName)
    {
        var content = LoadTxtFileFromEmbedded(resourcePrefix, fileName);
        return ParseItemTypes(content);
    }

    private static ItemTypeEntry[] LoadItemTypesFromDirectory(string directory)
    {
        var content = LoadTxtFileFromDirectory(directory, "itemtypes.txt");
        return ParseItemTypes(content);
    }

    private static ItemTypeEntry[] ParseItemTypes(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return [];

        var header = ParseHeader(lines[0]);
        var types = new List<ItemTypeEntry>();
        var nameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Get column indices
        int colCode = header.GetValueOrDefault("Code", -1);
        int colEquiv1 = header.GetValueOrDefault("Equiv1", -1);
        int colEquiv2 = header.GetValueOrDefault("Equiv2", -1);
        int colVarInvGfx = header.GetValueOrDefault("VarInvGfx", -1);

        // First pass: load all types and build name -> index mapping
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var columns = line.Split('\t');
            var codeStr = GetColumn(columns, colCode);

            // Skip empty or "Expansion" marker rows
            if (string.IsNullOrEmpty(codeStr))
            {
                types.Add(new ItemTypeEntry()); // Add placeholder to maintain indices
                continue;
            }

            var entry = new ItemTypeEntry
            {
                Code = ItemTypeCodes.MakeCode(codeStr),
                Equiv1Name = GetColumn(columns, colEquiv1),
                Equiv2Name = GetColumn(columns, colEquiv2),
                VarInvGfx = ParseByte(GetColumn(columns, colVarInvGfx))
            };

            int index = types.Count;
            types.Add(entry);

            // Map by code string for resolving Equiv references
            if (!string.IsNullOrEmpty(codeStr))
                nameToIndex.TryAdd(codeStr, index);
        }

        // Second pass: resolve Equiv1/Equiv2 names to indices
        for (int i = 0; i < types.Count; i++)
        {
            var entry = types[i];

            if (!string.IsNullOrEmpty(entry.Equiv1Name) && nameToIndex.TryGetValue(entry.Equiv1Name, out var equiv1Index))
                entry.Equiv1 = (ushort)equiv1Index;
            else
                entry.Equiv1 = 0xFFFF;

            if (!string.IsNullOrEmpty(entry.Equiv2Name) && nameToIndex.TryGetValue(entry.Equiv2Name, out var equiv2Index))
                entry.Equiv2 = (ushort)equiv2Index;
            else
                entry.Equiv2 = 0xFFFF;

            types[i] = entry;
        }

        return [.. types];
    }

    private static ItemEntry[] LoadItemsFromEmbedded(string resourcePrefix, string fileName, ItemTypeEntry[] itemTypes)
    {
        var content = LoadTxtFileFromEmbedded(resourcePrefix, fileName);
        return ParseItems(content, itemTypes);
    }

    private static ItemEntry[] LoadItemsFromDirectory(string directory, string fileName, ItemTypeEntry[] itemTypes)
    {
        var content = LoadTxtFileFromDirectory(directory, fileName);
        return ParseItems(content, itemTypes);
    }

    private static ItemEntry[] ParseItems(string content, ItemTypeEntry[] itemTypes)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return [];

        var header = ParseHeader(lines[0]);
        var items = new List<ItemEntry>();

        // Get column indices
        int colCode = header.GetValueOrDefault("code", -1);
        int colType = header.GetValueOrDefault("type", -1);
        int colCompactSave = header.GetValueOrDefault("compactsave", -1);
        int colStackable = header.GetValueOrDefault("stackable", -1);
        int colQuest = header.GetValueOrDefault("quest", -1);
        int colQuestDiffCheck = header.GetValueOrDefault("questdiffcheck", -1);

        // Build type name -> index mapping for resolving type references
        var typeNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < itemTypes.Length; i++)
        {
            var code = itemTypes[i].Code;
            if (code != 0)
            {
                // Convert code back to string for matching
                var codeStr = new string(new[]
                {
                    (char)(code & 0xFF),
                    (char)((code >> 8) & 0xFF),
                    (char)((code >> 16) & 0xFF),
                    (char)((code >> 24) & 0xFF)
                }).TrimEnd('\0', ' ');
                typeNameToIndex.TryAdd(codeStr, i);
            }
        }

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var columns = line.Split('\t');
            var codeStr = GetColumn(columns, colCode);

            if (string.IsNullOrEmpty(codeStr))
                continue;

            // Resolve type name to index
            var typeName = GetColumn(columns, colType);
            int typeIndex = 0;
            if (!string.IsNullOrEmpty(typeName) && typeNameToIndex.TryGetValue(typeName, out var ti))
                typeIndex = ti;

            var entry = new ItemEntry
            {
                Code = ItemTypeCodes.MakeCode(codeStr),
                Type = (ushort)typeIndex,
                CompactSave = ParseByte(GetColumn(columns, colCompactSave)),
                Stackable = ParseByte(GetColumn(columns, colStackable)),
                Quest = ParseByte(GetColumn(columns, colQuest)),
                QuestDiffCheck = ParseByte(GetColumn(columns, colQuestDiffCheck))
            };

            items.Add(entry);
        }

        return [.. items];
    }

    /// <summary>
    /// Gets the version data for the given save version.
    /// Throws if the required version data is not available.
    /// </summary>
    private VersionData GetVersionData(uint saveVersion)
    {
        if (_versionData.TryGetValue(saveVersion, out var data))
            return data;

        var availableVersions = string.Join(", ", _versionData.Keys.OrderBy(k => k));
        throw new InvalidOperationException(
            $"No txt data available for version {saveVersion}. " +
            $"Available versions: {availableVersions}");
    }

    public StatInfo GetStatInfo(StatId statId, uint saveVersion)
    {
        var data = GetVersionData(saveVersion);
        var index = (int)statId;
        if (index < 0 || index >= data.ItemStatCost.Length)
            throw new ArgumentOutOfRangeException(nameof(statId), $"Stat ID {statId} out of range");

        ref readonly var stat = ref data.ItemStatCost[index];
        return new StatInfo(
            CsvBits: stat.CsvBits,
            CsvParam: stat.CsvParam,
            CsvSigned: stat.CsvSigned,
            SaveBits: stat.SaveBits,
            SaveParamBits: stat.SaveParamBits,
            SaveAdd: stat.SaveAdd,
            ValShift: stat.ValShift
        );
    }

    public int GetItemIndex(uint itemCode, uint saveVersion)
    {
        var data = GetVersionData(saveVersion);
        return data.ItemCodeToIndex.GetValueOrDefault(itemCode, -1);
    }

    public ItemTypeInfo GetItemInfo(int itemIndex, uint saveVersion)
    {
        var data = GetVersionData(saveVersion);
        if (itemIndex < 0 || itemIndex >= data.Items.Length)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), $"Item index {itemIndex} out of range");

        ref readonly var item = ref data.Items[itemIndex];

        // Get the item's primary type index
        var typeIndex = item.Type;
        var hasVariableGfx = false;

        if (typeIndex < data.ItemTypes.Length)
        {
            hasVariableGfx = data.ItemTypes[typeIndex].VarInvGfx > 0;
        }

        uint typeCode = typeIndex < data.ItemTypes.Length ? data.ItemTypes[typeIndex].Code : 0;

        return new ItemTypeInfo(
            CompactSave: item.CompactSave != 0,
            HasVariableGfx: hasVariableGfx,
            IsArmor: data.IsDescendant(ItemTypeCodes.Armor, typeCode),
            IsWeapon: data.IsDescendant(ItemTypeCodes.Weapon, typeCode),
            IsGold: data.IsDescendant(ItemTypeCodes.Gold, typeCode),
            IsStackable: item.Stackable != 0,
            IsCharm: data.IsDescendant(ItemTypeCodes.Charm, typeCode),
            IsBodyPart: data.IsDescendant(ItemTypeCodes.BodyPart, typeCode),
            IsPlayerBodyPart: data.IsDescendant(ItemTypeCodes.PlayerBodyPart, typeCode),
            IsScrollOrBook: data.IsDescendant(ItemTypeCodes.Scroll, typeCode) ||
                            data.IsDescendant(ItemTypeCodes.Book, typeCode),
            IsQuest: item.Quest != 0,
            QuestDiffCheck: item.QuestDiffCheck != 0
        );
    }

    private static Dictionary<uint, HashSet<uint>> BuildTypeDescendants(ItemTypeEntry[] itemTypes)
    {
        var children = new List<int>[itemTypes.Length];
        for (int i = 0; i < itemTypes.Length; i++)
        {
            children[i] = [];
        }

        for (int i = 0; i < itemTypes.Length; i++)
        {
            ref readonly var itemType = ref itemTypes[i];
            if (itemType.Equiv1 != 0 && itemType.Equiv1 != 0xFFFF)
                children[itemType.Equiv1].Add(i);
            if (itemType.Equiv2 != 0 && itemType.Equiv2 != 0xFFFF)
                children[itemType.Equiv2].Add(i);
        }

        var descendantsByIndex = new HashSet<uint>?[itemTypes.Length];

        HashSet<uint> ComputeDescendants(int index)
        {
            var cached = descendantsByIndex[index];
            if (cached != null)
                return cached;

            var set = new HashSet<uint>();
            uint code = itemTypes[index].Code;
            if (code != 0)
                set.Add(code);

            foreach (int child in children[index])
            {
                uint childCode = itemTypes[child].Code;
                if (childCode != 0)
                    set.Add(childCode);
                set.UnionWith(ComputeDescendants(child));
            }

            descendantsByIndex[index] = set;
            return set;
        }

        var result = new Dictionary<uint, HashSet<uint>>();
        for (int i = 0; i < itemTypes.Length; i++)
        {
            uint code = itemTypes[i].Code;
            if (code == 0)
                continue;

            var set = ComputeDescendants(i);
            if (!result.TryGetValue(code, out var existing))
            {
                result[code] = set;
            }
            else
            {
                existing.UnionWith(set);
            }
        }

        return result;
    }

    // Internal data structures
    private class VersionData
    {
        public required ItemStatCostEntry[] ItemStatCost { get; init; }
        public required ItemTypeEntry[] ItemTypes { get; init; }
        public required ItemEntry[] Items { get; init; }
        public required Dictionary<uint, int> ItemCodeToIndex { get; init; }
        public required Dictionary<uint, HashSet<uint>> TypeDescendants { get; init; }

        public bool IsDescendant(uint targetTypeCode, uint typeCode)
        {
            return typeCode != 0 &&
                   TypeDescendants.TryGetValue(targetTypeCode, out var set) &&
                   set.Contains(typeCode);
        }
    }

    private struct ItemStatCostEntry
    {
        public bool CsvSigned;
        public byte CsvBits;
        public byte CsvParam;
        public byte SaveBits;
        public int SaveParamBits;
        public int SaveAdd;
        public byte ValShift;
    }

    private struct ItemTypeEntry
    {
        public uint Code;
        public string Equiv1Name;
        public string Equiv2Name;
        public ushort Equiv1;
        public ushort Equiv2;
        public byte VarInvGfx;
    }

    private struct ItemEntry
    {
        public uint Code;
        public ushort Type;
        public byte CompactSave;
        public byte Stackable;
        public byte Quest;
        public byte QuestDiffCheck;
    }
}
