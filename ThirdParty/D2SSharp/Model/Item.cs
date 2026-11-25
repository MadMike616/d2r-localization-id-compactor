using D2SSharp.Data;
using D2SSharp.Enums;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// A single item with all its data.
/// </summary>
public partial class Item
{
    /// <summary>Item magic header: "JM" (0x4D4A)</summary>
    public const ushort Magic = 0x4D4A;

    /// <summary>Item flags.</summary>
    public ItemFlags Flags { get; set; }

    /// <summary>Item format version.</summary>
    public ushort Version { get; set; }

    /// <summary>Item position/location.</summary>
    public ItemPosition Position { get; set; } = new();

    /// <summary>4-character item code as uint (e.g., 'cap ' = 0x20706163).</summary>
    public uint ItemCode { get; set; }

    /// <summary>Item code as a string.</summary>
    public string ItemCodeString
    {
        get
        {
            Span<char> chars = stackalloc char[4];
            chars[0] = (char)(ItemCode & 0xFF);
            chars[1] = (char)((ItemCode >> 8) & 0xFF);
            chars[2] = (char)((ItemCode >> 16) & 0xFF);
            chars[3] = (char)((ItemCode >> 24) & 0xFF);
            return new string(chars).TrimEnd(' ', '\0');
        }
        set
        {
            Span<char> padded = stackalloc char[4];
            padded.Fill(' ');
            value.AsSpan()[..Math.Min(value.Length, 4)].CopyTo(padded);
            ItemCode = (uint)(padded[0] | (padded[1] << 8) | (padded[2] << 16) | (padded[3] << 24));
        }
    }

    // --- Ear data (IsEar flag) ---
    /// <summary>Ear owner's class file index (ear items only, 3 bits).</summary>
    public byte EarFileIndex { get; set; }

    /// <summary>Ear owner's level (ear items only).</summary>
    public byte EarLevel { get; set; }

    /// <summary>Ear owner's name (ear items only).</summary>
    public string EarName { get; set; } = "";

    // --- Gold data ---
    /// <summary>Gold amount (gold items only).</summary>
    public uint GoldAmount { get; set; }
    public bool PlayerGold { get; set; }

    // --- Quest item data (compact) ---
    /// <summary>Difficulty the quest item was found in (compact quest items with diff check).</summary>
    public Difficulty? QuestDifficulty { get; set; }

    /// <summary>Item random seed.</summary>
    public uint ItemSeed { get; set; }

    /// <summary>Item level.</summary>
    public byte ItemLevel { get; set; }

    /// <summary>Item quality.</summary>
    public ItemQuality Quality { get; set; }

    /// <summary>Variable graphics index (if has variable gfx).</summary>
    public byte? VariableGfxId { get; set; }

    /// <summary>Auto affix ID (if has auto affix).</summary>
    public ushort? AutoAffixId { get; set; }

    /// <summary>Quality-specific data.</summary>
    public ItemQualityData? QualityData { get; set; }

    /// <summary>Runeword ID (if runeword).</summary>
    public ushort? RunewordId { get; set; }

    // Local repair instrumentation: offsets in the input BitReader, never
    // serialized. The repair patches these bits in the original save bytes.
    public int? RunewordIdBitOffset { get; private set; }

    /// <summary>Personalized name (if personalized).</summary>
    public string PersonalizedName { get; set; } = "";

    /// <summary>
    /// Opaque 128-bit realm data. Never generated or interpreted by the client.
    /// Version > 96: 4 x uint32. Versions 87-96: 2 x uint32 (+1 uint32 discarded by the game for v94-96),
    /// which the game maps to elements [2] and [3] of the 128-bit value.
    /// </summary>
    public uint[]? RealmData { get; set; }

    /// <summary>Defense value (armor only).</summary>
    public int? Defense { get; set; }

    /// <summary>Maximum durability.</summary>
    public int? MaxDurability { get; set; }

    /// <summary>Current durability.</summary>
    public int? Durability { get; set; }

    /// <summary>Stack quantity (stackable items).</summary>
    public int? Quantity { get; set; }

    /// <summary>Set item bonus mask (set items only).</summary>
    public byte? SetItemMask { get; set; }

    /// <summary>Item stat list.</summary>
    public List<Stat> Stats { get; set; } = [];

    /// <summary>Set bonus stat lists (up to 5 additional lists for set items).</summary>
    public List<List<Stat>> SetBonusStats { get; set; } = [];

    /// <summary>Runeword stat list (if runeword).</summary>
    public List<Stat>? RunewordStats { get; set; }

    /// <summary>Socket count, and socketed items.</summary>
    public List<Item?> Sockets { get; set; } = [];

    // --- v100+ chronicle data (IFLAG_CHRONICLE, flag bit 28 = 0x10000000) ---
    /// <summary>
    /// String table ID of the name of whatever dropped the item (16 bits, v100+ with chronicle flag).
    /// Becomes <see cref="ChronicleEntry.Source"/> when the chronicle entry is recorded.
    /// </summary>
    public ushort? ChronicleId { get; set; }
    public int? ChronicleIdBitOffset { get; private set; }

    /// <summary>When the item dropped, in minutes since the Unix epoch, UTC (32 bits, when IFLAG_CHRONICLE_COMPACT bit 29 is NOT set).</summary>
    public uint? ChronicleTimestamp { get; set; }

    /// <summary>64-bit game account IDs that receive the chronicle entry when the item is identified (up to 8, v100+).</summary>
    public List<ulong>? ChronicleRecipients { get; set; }

    // --- v100+ advanced stash data (AdvancedStashStackable column in items.txt) ---
    /// <summary>Stack size for items in advanced stash tabs (8 bits, v100+).</summary>
    public byte? AdvancedStashStackSize { get; set; }

    /// <summary>
    /// Reads an item from a BitReader.
    /// Matches C++ ITEMS_DecodeItemFromBitstream structure.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: 96 uses D2R format).</param>
    public static Item Read(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        var item = new Item();
        item.ReadFrom(ref reader, externalData, saveVersion);
        return item;
    }

    /// <summary>
    /// Reads item data from a BitReader into this instance.
    /// Matches C++ ITEMS_DecodeItemFromBitstream structure.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public void ReadFrom(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        if (saveVersion <= 96)
        {
            // Verify magic "JM"
            ushort magic = reader.ReadUInt16();
            if (magic != Magic)
                throw new InvalidDataException($"Invalid item magic: 0x{magic:X4}");
        }

        // Read flags
        var flags = (ItemFlags)reader.ReadUInt32();

        // C++: If LOWQUALITY/Gamble flag is set, remember it and clear the flag
        bool isGamble = flags.HasFlag(ItemFlags.LowQuality);
        if (isGamble)
        {
            flags &= ~ItemFlags.LowQuality;
        }

        // C++: Clear INIT flag
        flags &= ~ItemFlags.Init;

        Flags = flags;

        // Branch based on CompactSave flag (matches C++ structure)
        try
        {
            if (flags.HasFlag(ItemFlags.CompactSave))
            {
                ReadCompactItem(ref reader, externalData, saveVersion);
            }
            else
            {
                ReadCompleteItem(ref reader, externalData, isGamble, saveVersion);
            }
        }
        catch (Exception ex) when (ex is not InvalidDataException || !ex.Message.Contains("item code="))
        {
            throw new InvalidDataException(
                $"Error parsing item code={ItemCodeString} flags=0x{(uint)Flags:X8} compact={flags.HasFlag(ItemFlags.CompactSave)} quality={Quality} at bit {reader.BitPosition}: {ex.Message}", ex);
        }

        // C++: Each item uses a fresh byte buffer, so items are byte-aligned
        // BITMANIP_GetSize returns (nPosBits ? nPos + 1 : nPos) - i.e., byte count rounded up
        reader.AlignToByte();

        // Read socketed items immediately after the parent item
        foreach (var socket in Sockets)
        {
            socket?.ReadFrom(ref reader, externalData, saveVersion);
        }
    }

    /// <summary>
    /// Reads a compact item. Matches C++ ITEMS_DecodeItemBitstreamCompact.
    /// </summary>
    private void ReadCompactItem(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        // Read item format version
        Version = ReadItemFormatVersion(ref reader, saveVersion);

        // Read mode and position
        var mode = (ItemMode)reader.ReadBits(3);
        Position = ItemPosition.Read(ref reader, mode);

        if (Flags.HasFlag(ItemFlags.IsEar))
        {
            // Ear item (compact format)
            ItemCode = 0x20726165; // 'ear '
            EarFileIndex = (byte)reader.ReadBits(3);
            EarLevel = (byte)reader.ReadBits(7);
            EarName = ReadString(ref reader, saveVersion);
        }
        else
        {
            // Regular compact item
            ItemCode = ReadItemCode(ref reader, saveVersion);

            // Gold handling (in compact format)
            int itemIndex = externalData.GetItemIndex(ItemCode, saveVersion);
            var itemInfo = externalData.GetItemInfo(itemIndex, saveVersion);
            if (itemInfo.IsGold)
            {
                bool bigGold = reader.ReadBool();
                GoldAmount = bigGold ? reader.ReadUInt32() : reader.ReadBits(12);
                if (saveVersion > 96)
                    PlayerGold = reader.ReadBool();
            }

            // Should check if item type is Scroll type, and set QualityData for scrolls (which is the first magic
            // prefix being 0 or 1 identifying if it's a town portal scroll or identification scroll).
            // However, that is derived from item code, hence has no effect on serialization.
        }

        // Quest difficulty stat (version > 92, quest items with nQuestDiffCheck)
        // C++: value = (raw - SaveAdd) << ValShift
        int compactItemIndex = externalData.GetItemIndex(ItemCode, saveVersion);
        var compactItemInfo = externalData.GetItemInfo(compactItemIndex, saveVersion);
        if (saveVersion > 92 && compactItemInfo.IsQuest && compactItemInfo.QuestDiffCheck)
        {
            var questDiffStat = externalData.GetStatInfo(StatId.QuestItemDifficulty, saveVersion);
            int rawValue = (int)reader.ReadBits(questDiffStat.SaveBits);
            QuestDifficulty = (Difficulty)((rawValue - questDiffStat.SaveAdd) << questDiffStat.ValShift);
        }

        // Realm data (version > 86, bCheckForHeader=true for save files)
        ReadRealmData(ref reader, saveVersion);

        // Advanced stash stack size (v100+, sub_140276B60)
        ReadAdvancedStashStackSize(ref reader, saveVersion);

        // C++ sets default values for compact items
        ItemLevel = 1;
        Quality = ItemQuality.Normal;
    }

    /// <summary>
    /// Reads a complete item. Matches C++ ITEMS_DecodeItemBitstreamComplete.
    /// </summary>
    private void ReadCompleteItem(ref BitReader reader, IExternalData externalData, bool isGamble, uint saveVersion)
    {
        // Read item format version
        Version = ReadItemFormatVersion(ref reader, saveVersion);

        // Read mode and position
        var mode = (ItemMode)reader.ReadBits(3);
        Position = ItemPosition.Read(ref reader, mode);

        // Read item code
        ItemCode = ReadItemCode(ref reader, saveVersion);

        // C++: If isGamble (was LOWQUALITY flag), return early with inferior quality
        if (isGamble)
        {
            ItemLevel = 1;
            Quality = ItemQuality.Inferior;
            return;
        }

        // Socketed items count
        var socketedItemCount = (int)reader.ReadBits(3);
        Sockets = [.. Enumerable.Range(0, socketedItemCount).Select(_ => new Item())];

        // Item seed (bServer=true for save files, always present)
        ItemSeed = reader.ReadUInt32();

        // Item level
        ItemLevel = (byte)reader.ReadBits(7);
        if (ItemLevel < 1)
            ItemLevel = 1;

        // Quality
        Quality = (ItemQuality)reader.ReadBits(4);

        // Variable graphics
        if (reader.ReadBool())
        {
            VariableGfxId = (byte)reader.ReadBits(3);
        }

        // Auto affix (C++: adds nAutoMagicOffset after reading)
        if (reader.ReadBool())
        {
            int rawAutoAffix = (int)reader.ReadBits(11);
            AutoAffixId = (ushort)(rawAutoAffix + externalData.GetAutoMagicOffset(saveVersion));
        }
        // Quality-specific data
        int itemIndex = externalData.GetItemIndex(ItemCode, saveVersion);
        var itemInfo = externalData.GetItemInfo(itemIndex, saveVersion);
        QualityData = ReadQualityData(ref reader, Quality, itemInfo, externalData.GetMagicPrefixOffset(saveVersion));

        // Runeword ID
        if (Flags.HasFlag(ItemFlags.Runeword))
        {
            RunewordIdBitOffset = reader.BitPosition;
            RunewordId = (ushort)reader.ReadBits(16);
        }

        // Ear data OR Personalization (mutually exclusive in C++)
        // C++: if (IFLAG_ISEAR) { ... } else if (IFLAG_PERSONALIZED) { ... }
        if (Flags.HasFlag(ItemFlags.IsEar))
        {
            // Ear item in complete format (rare but possible)
            EarFileIndex = (byte)reader.ReadBits(3);
            EarLevel = (byte)reader.ReadBits(7);
            EarName = ReadString(ref reader, saveVersion);
        }
        else if (Flags.HasFlag(ItemFlags.Personalized))
        {
            PersonalizedName = ReadString(ref reader, saveVersion);
        }
        // Realm data (bServer=true for save files, version > 86)
        ReadRealmData(ref reader, saveVersion);

        // Type-specific data
        if (itemInfo.IsArmor)
        {
            // Armor: defense + durability (bit widths and SaveAdd from ItemStatCost.txt)
            // C++: value = raw - SaveAdd for all three stats
            var armorClassStat = externalData.GetStatInfo(StatId.ArmorClass, saveVersion);
            var maxDurabilityStat = externalData.GetStatInfo(StatId.MaxDurability, saveVersion);
            var durabilityStat = externalData.GetStatInfo(StatId.Durability, saveVersion);

            Defense = (int)reader.ReadBits(armorClassStat.SaveBits) - armorClassStat.SaveAdd;
            MaxDurability = (int)reader.ReadBits(maxDurabilityStat.SaveBits) - maxDurabilityStat.SaveAdd;
            if (MaxDurability > 0)
            {
                Durability = (int)reader.ReadBits(durabilityStat.SaveBits) - durabilityStat.SaveAdd;
            }
        }
        else if (itemInfo.IsWeapon)
        {
            // Weapon: durability only (bit widths and SaveAdd from ItemStatCost.txt)
            // C++: value = raw - SaveAdd
            var maxDurabilityStat = externalData.GetStatInfo(StatId.MaxDurability, saveVersion);
            var durabilityStat = externalData.GetStatInfo(StatId.Durability, saveVersion);

            MaxDurability = (int)reader.ReadBits(maxDurabilityStat.SaveBits) - maxDurabilityStat.SaveAdd;
            if (MaxDurability > 0)
            {
                Durability = (int)reader.ReadBits(durabilityStat.SaveBits) - durabilityStat.SaveAdd;
            }
        }
        else if (itemInfo.IsGold)
        {
            // Gold: variable bit-length amount
            bool bigGold = reader.ReadBool();
            GoldAmount = bigGold ? reader.ReadUInt32() : reader.ReadBits(12);
            if (saveVersion > 96)
                PlayerGold = reader.ReadBool();
        }

        // Stackable quantity
        // v105+: 1-bit presence flag for ALL items, then optional 9-bit quantity
        // v<=104: only stackable items get quantity (bit width is version-dependent)
        if (saveVersion > 104)
        {
            if (reader.ReadBool())
                Quantity = (int)reader.ReadBits(9);
        }
        else if (itemInfo.IsStackable)
        {
            Quantity = (int)reader.ReadBits(externalData.GetQuantityBits(saveVersion));
        }

        // Socket count (bit width from ItemStatCost.txt)
        if (Flags.HasFlag(ItemFlags.Socketed))
        {
            var numSocketsStat = externalData.GetStatInfo(StatId.NumSockets, saveVersion);
            // Socket count
            var socketCount = (int)reader.ReadBits(numSocketsStat.SaveBits);
            Sockets.AddRange(new Item?[socketCount - Sockets.Count]);
        }

        // Stats (bServer=true or identified)
        // Set item mask
        if (Quality == ItemQuality.Set)
        {
            SetItemMask = (byte)reader.ReadBits(5);
        }

        // Read main stat list
        Stats = ReadStatList(ref reader, externalData, saveVersion);

        // Set bonus stat lists (for each bit in SetItemMask)
        if (Quality == ItemQuality.Set && SetItemMask.HasValue)
        {
            for (int i = 0; i < 5; i++)
            {
                if ((SetItemMask.Value & (1 << i)) != 0)
                {
                    SetBonusStats.Add(ReadStatList(ref reader, externalData, saveVersion));
                }
            }
        }

        // Runeword stats
        if (Flags.HasFlag(ItemFlags.Runeword))
        {
            RunewordStats = ReadStatList(ref reader, externalData, saveVersion);
        }

        // Chronicle data (v100+, IFLAG_CHRONICLE = bit 28)
        ReadChronicle(ref reader, saveVersion);

        // Advanced stash stack size (v100+, sub_140276B60)
        ReadAdvancedStashStackSize(ref reader, saveVersion);
    }

    /// <summary>
    /// Reads quality-specific data based on item quality.
    /// Handles Normal quality special cases (charms, body parts, scrolls/books).
    /// Note: For save files (version 96), quality data is ALWAYS present regardless of identification.
    /// C++ uses condition: (bServer || identified), and bServer=TRUE for save files.
    /// </summary>
    private static ItemQualityData? ReadQualityData(ref BitReader reader, ItemQuality quality, ItemTypeInfo itemInfo, int magicPrefixOffset)
    {
        // For save files, quality data is always present (C++: bServer || identified, bServer=TRUE for saves)
        return quality switch
        {
            ItemQuality.Inferior => InferiorQualityData.Read(ref reader),
            ItemQuality.Superior => SuperiorQualityData.Read(ref reader),
            ItemQuality.Magic => MagicQualityData.Read(ref reader, magicPrefixOffset),
            ItemQuality.Set => SetUniqueQualityData.Read(ref reader),
            ItemQuality.Rare => RareCraftQualityData.Read(ref reader, magicPrefixOffset),
            ItemQuality.Unique => SetUniqueQualityData.Read(ref reader),
            ItemQuality.Craft => RareCraftQualityData.Read(ref reader, magicPrefixOffset),
            ItemQuality.Tempered => TemperedQualityData.Read(ref reader),
            ItemQuality.Normal => ReadNormalQualityData(ref reader, itemInfo, magicPrefixOffset),
            _ => null
        };
    }

    /// <summary>
    /// Reads Normal quality special cases: charms, body parts, scrolls/books.
    /// Note: For save files, charm data is always present (C++: bServer || identified).
    /// </summary>
    private static ItemQualityData? ReadNormalQualityData(ref BitReader reader, ItemTypeInfo itemInfo, int magicPrefixOffset)
    {
        // Charm: has magic prefix or suffix stored specially
        // For save files, always present (C++: bCheckForHeader || identified, bCheckForHeader=TRUE for saves)
        if (itemInfo.IsCharm)
        {
            return NormalCharmQualityData.Read(ref reader, magicPrefixOffset);
        }

        // Monster body part (not player body part): file index
        // Note: Body parts are always written regardless of identification
        if (itemInfo.IsBodyPart && !itemInfo.IsPlayerBodyPart)
        {
            return NormalBodyPartQualityData.Read(ref reader);
        }

        // Scroll or book: spell type
        // Note: Scrolls/books are always written regardless of identification
        if (itemInfo.IsScrollOrBook)
        {
            return NormalScrollBookQualityData.Read(ref reader);
        }

        return null;
    }

    /// <summary>
    /// Reads a stat list with paired stat handling.
    /// Returns the list of parsed stats plus optional raw trailing bit data for unknown stats.
    /// </summary>
    private static List<Stat> ReadStatList(ref BitReader reader, IExternalData externalData, uint saveVersion)
    {
        var stats = new List<Stat>();
        StatId? previousId = null;
        StatInfo previousInfo = default;
        int previousStart = 0;

        while (true)
        {
            int statStart = reader.BitPosition;
            StatId statId = (StatId)reader.ReadBits(9);
            if (statId == StatId.Terminator)
                break;

            StatInfo info;
            try { info = externalData.GetStatInfo(statId, saveVersion); }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentOutOfRangeException)
            {
                string previous = previousId.HasValue
                    ? $" Previous stat ID {(uint)previousId.Value} at bit {previousStart} used {previousInfo.SaveBits} value bits and {previousInfo.SaveParamBits} parameter bits."
                    : " No preceding stat in this list was decoded.";
                throw new InvalidDataException(
                    $"Cannot decode stat ID {(uint)statId} at bit {statStart} using the selected mod tables." + previous +
                    " The save and mod tables may describe different stat layouts. No IDs or bit widths were guessed. " + error.Message, error);
            }
            int layer = 0;
            if (info.SaveParamBits > 0)
            {
                layer = (int)reader.ReadBits(info.SaveParamBits);
            }

            // C++: value = (raw - SaveAdd) << ValShift
            int rawValue = (int)reader.ReadBits(info.SaveBits) - info.SaveAdd;
            long value = (long)rawValue << info.ValShift;
            stats.Add(new Stat { Id = statId, Layer = layer, Value = value });

            // Handle paired stats - read additional stats that follow without their own ID
            var pairedStats = statId.GetPairedStats();
            foreach (StatId pairedStatId in pairedStats)
            {
                var pairedInfo = externalData.GetStatInfo(pairedStatId, saveVersion);

                // C++: value = (raw - SaveAdd) << ValShift
                int pairedRaw = (int)reader.ReadBits(pairedInfo.SaveBits) - pairedInfo.SaveAdd;
                long pairedValue = (long)pairedRaw << pairedInfo.ValShift;
                stats.Add(new Stat { Id = pairedStatId, Value = pairedValue });
            }

            // C++: After reading poison damage, sets STAT_POISON_COUNT = 1
            if (statId == StatId.PoisonMinDamage)
            {
                stats.Add(new Stat { Id = StatId.PoisonCount, Value = 1 });
            }
            previousId = statId;
            previousInfo = info;
            previousStart = statStart;
        }

        return stats;
    }

    /// <summary>
    /// Writes an item to a BitWriter.
    /// Matches C++ ITEMS_SerializeItemToBitstream structure.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data.</param>
    /// <param name="saveVersion">Save file version (determines format: >96 uses D2R format).</param>
    public void Write(ref BitWriter writer, IExternalData externalData, uint saveVersion)
    {
        if (saveVersion <= 96)
        {
            // Write magic "JM"
            writer.WriteUInt16(Magic);
        }

        // Prepare flags for writing (C++: set JUSTSAVED, clear INIT)
        var writeFlags = Flags | ItemFlags.JustSaved;
        writeFlags &= ~ItemFlags.Init;

        // C++: Set CompactSave based on item type if item info available
        int itemIndex = externalData.GetItemIndex(ItemCode, saveVersion);
        var itemInfo = externalData.GetItemInfo(itemIndex, saveVersion);
        if (itemInfo.CompactSave)
        {
            writeFlags |= ItemFlags.CompactSave;
        }

        // Write flags
        writer.WriteUInt32((uint)writeFlags);

        if (writeFlags.HasFlag(ItemFlags.CompactSave))
        {
            WriteCompactItem(ref writer, externalData, itemInfo, saveVersion);
        }
        else
        {
            WriteCompleteItem(ref writer, externalData, itemInfo, saveVersion);
        }

        // C++: Each item uses a fresh byte buffer, so items are byte-aligned
        // BITMANIP_GetSize returns (nPosBits ? nPos + 1 : nPos) - i.e., byte count rounded up
        writer.AlignToByte();

        // Write socketed items immediately after the parent item
        foreach (var socketedItem in Sockets)
        {
            socketedItem?.Write(ref writer, externalData, saveVersion);
        }
    }

    private void WriteCompactItem(ref BitWriter writer, IExternalData externalData, ItemTypeInfo itemInfo, uint saveVersion)
    {
        // Write item format version
        WriteItemFormat(ref writer, Version, saveVersion);
        // C++: clamps nAnimMode to 0-7 (3 bits)
        writer.WriteBits(Math.Min((uint)Position.Mode, 7u), 3);
        Position.Write(ref writer);

        if (Flags.HasFlag(ItemFlags.IsEar))
        {
            // Ear item
            // C++: clamps EarFileIndex to 0-7 (3 bits), EarLevel to 0-127 (7 bits)
            writer.WriteBits(Math.Clamp(EarFileIndex, (byte)0, (byte)7), 3);
            writer.WriteBits(Math.Clamp(EarLevel, (byte)0, (byte)127), 7);
            WriteString(ref writer, EarName, saveVersion);
        }
        else
        {
            // Regular compact item
            WriteItemCode(ref writer, ItemCode, saveVersion);

            // Gold handling
            if (itemInfo.IsGold)
            {
                bool bigGold = GoldAmount >= 4096;
                writer.WriteBool(bigGold);
                if (bigGold)
                    writer.WriteUInt32(GoldAmount);
                else
                    writer.WriteBits(GoldAmount, 12);
                if (saveVersion > 96)
                    writer.WriteBool(PlayerGold);
            }
        }

        // Quest difficulty stat
        // C++: raw = (value >> ValShift) + SaveAdd
        if (saveVersion > 92 && itemInfo.IsQuest && itemInfo.QuestDiffCheck)
        {
            var questDiffStat = externalData.GetStatInfo(StatId.QuestItemDifficulty, saveVersion);
            int rawValue = ((int)(QuestDifficulty ?? 0) >> questDiffStat.ValShift) + questDiffStat.SaveAdd;
            writer.WriteBits((uint)rawValue, questDiffStat.SaveBits);
        }

        // Realm data
        WriteRealmData(ref writer, saveVersion);

        // Advanced stash stack size (v100+)
        WriteAdvancedStashStackSize(ref writer, saveVersion);
    }

    private void WriteCompleteItem(ref BitWriter writer, IExternalData externalData, ItemTypeInfo itemInfo, uint saveVersion)
    {
        // Write item format version
        WriteItemFormat(ref writer, Version, saveVersion);
        // C++: clamps nAnimMode to 0-7 (3 bits)
        writer.WriteBits(Math.Min((uint)Position.Mode, 7u), 3);
        Position.Write(ref writer);

        WriteItemCode(ref writer, ItemCode, saveVersion);

        // C++: Clamp NumSocketedItems to 0-7 (3 bits)
        writer.WriteBits((uint)Math.Clamp(Sockets.Count(o => o != null), 0, 7), 3);
        writer.WriteUInt32(ItemSeed);
        // C++: Clamp ItemLevel to 1-127 (7 bits)
        writer.WriteBits((uint)Math.Clamp((int)ItemLevel, 1, 127), 7);
        writer.WriteBits((uint)Quality, 4);

        // Variable graphics (C++: presence based on item type capability, not value)
        writer.WriteBool(itemInfo.HasVariableGfx);
        if (itemInfo.HasVariableGfx)
        {
            // C++: Clamp to 0-7 (3 bits), defaults to 0 if no value
            int gfxIndex = Math.Clamp((int)(VariableGfxId ?? 0), 0, 7);
            writer.WriteBits((uint)gfxIndex, 3);
        }

        // Auto affix (C++: subtracts nAutoMagicOffset before writing, presence based on adjusted value > 0)
        int autoAffixValue = AutoAffixId ?? 0;
        int adjustedAutoAffix = autoAffixValue > externalData.GetAutoMagicOffset(saveVersion)
            ? autoAffixValue - externalData.GetAutoMagicOffset(saveVersion)
            : autoAffixValue;
        writer.WriteBool(adjustedAutoAffix > 0);
        if (adjustedAutoAffix > 0)
        {
            writer.WriteBits((uint)adjustedAutoAffix, 11);
        }

        // Quality data (pass MagicPrefixOffset for prefix ID adjustment)
        QualityData?.Write(ref writer, externalData.GetMagicPrefixOffset(saveVersion));

        // Runeword ID (C++: always write if flag set, even if value is 0)
        if (Flags.HasFlag(ItemFlags.Runeword))
        {
            writer.WriteBits(RunewordId ?? 0, 16);
        }

        // Ear data OR Personalization (mutually exclusive)
        if (Flags.HasFlag(ItemFlags.IsEar))
        {
            // C++: clamps EarFileIndex to 0-7 (3 bits), EarLevel to 0-127 (7 bits)
            writer.WriteBits(Math.Clamp(EarFileIndex, (byte)0, (byte)7), 3);
            writer.WriteBits(Math.Clamp(EarLevel, (byte)0, (byte)127), 7);
            WriteString(ref writer, EarName, saveVersion);
        }
        else if (Flags.HasFlag(ItemFlags.Personalized))
        {
            WriteString(ref writer, PersonalizedName, saveVersion);
        }

        // Realm data
        WriteRealmData(ref writer, saveVersion);

        // Type-specific data (bit widths and SaveAdd from ItemStatCost.txt)
        if (itemInfo.IsArmor)
        {
            // Armor: defense + durability
            // C++: raw = value + SaveAdd (clamped to bit range)
            var armorClassStat = externalData.GetStatInfo(StatId.ArmorClass, saveVersion);
            var maxDurabilityStat = externalData.GetStatInfo(StatId.MaxDurability, saveVersion);
            var durabilityStat = externalData.GetStatInfo(StatId.Durability, saveVersion);

            // Defense: clamp semantic value, then add SaveAdd
            int defenseValue = Math.Clamp(Defense ?? 0, 0, armorClassStat.MaxSemanticValue);
            writer.WriteBits((uint)(defenseValue + armorClassStat.SaveAdd), armorClassStat.SaveBits);

            // MaxDurability: C++ always writes value + SaveAdd, only skips durability if semantic value is 0
            int maxDurSemantic = MaxDurability ?? 0;
            int maxDurClamped = Math.Clamp(maxDurSemantic, 0, maxDurabilityStat.MaxSemanticValue);
            writer.WriteBits((uint)(maxDurClamped + maxDurabilityStat.SaveAdd), maxDurabilityStat.SaveBits);

            // Durability: only written if maxDurability semantic value > 0
            if (maxDurSemantic > 0)
            {
                int durSemantic = Durability ?? 0;
                int durClamped = Math.Clamp(durSemantic, 0, durabilityStat.MaxSemanticValue);
                writer.WriteBits((uint)(durClamped + durabilityStat.SaveAdd), durabilityStat.SaveBits);
            }
        }
        else if (itemInfo.IsWeapon)
        {
            // Weapon: durability only
            // C++: raw = value + SaveAdd (clamped to bit range)
            var maxDurabilityStat = externalData.GetStatInfo(StatId.MaxDurability, saveVersion);
            var durabilityStat = externalData.GetStatInfo(StatId.Durability, saveVersion);

            // MaxDurability: C++ always writes value + SaveAdd, only skips durability if semantic value is 0
            int maxDurSemantic = MaxDurability ?? 0;
            int maxDurClamped = Math.Clamp(maxDurSemantic, 0, maxDurabilityStat.MaxSemanticValue);
            writer.WriteBits((uint)(maxDurClamped + maxDurabilityStat.SaveAdd), maxDurabilityStat.SaveBits);

            // Durability: only written if maxDurability semantic value > 0
            if (maxDurSemantic > 0)
            {
                int durSemantic = Durability ?? 0;
                int durClamped = Math.Clamp(durSemantic, 0, durabilityStat.MaxSemanticValue);
                writer.WriteBits((uint)(durClamped + durabilityStat.SaveAdd), durabilityStat.SaveBits);
            }
        }
        else if (itemInfo.IsGold)
        {
            // Gold: variable bit-length amount
            bool bigGold = GoldAmount >= 4096;
            writer.WriteBool(bigGold);
            if (bigGold)
                writer.WriteUInt32(GoldAmount);
            else
                writer.WriteBits(GoldAmount, 12);
            if (saveVersion > 96)
                writer.WriteBool(PlayerGold);
        }

        // Stackable quantity
        // v105+: 1-bit presence flag for ALL items, then optional 9-bit quantity
        // v<=104: only stackable items get quantity (bit width is version-dependent)
        if (saveVersion > 104)
        {
            bool hasQuantity = Quantity.HasValue;
            writer.WriteBool(hasQuantity);
            if (hasQuantity)
                writer.WriteBits((uint)Math.Clamp(Quantity!.Value, 0, 511), 9);
        }
        else if (itemInfo.IsStackable)
        {
            int maxQuantity = (1 << externalData.GetQuantityBits(saveVersion)) - 1;
            int quantityValue = Math.Clamp(Quantity ?? 0, 0, maxQuantity);
            writer.WriteBits((uint)quantityValue, externalData.GetQuantityBits(saveVersion));
        }

        // Socket count (bit width from ItemStatCost.txt)
        if (Flags.HasFlag(ItemFlags.Socketed))
        {
            var numSocketsStat = externalData.GetStatInfo(StatId.NumSockets, saveVersion);
            int maxSockets = (1 << numSocketsStat.SaveBits) - 1;
            int socketCount = Math.Clamp(Sockets.Count, 0, maxSockets);
            writer.WriteBits((uint)socketCount, numSocketsStat.SaveBits);
        }

        // Set item mask
        if (Quality == ItemQuality.Set)
        {
            writer.WriteBits(SetItemMask ?? 0, 5);
        }

        // Main stat list
        WriteStatList(ref writer, Stats, externalData, saveVersion);

        // Set bonus stat lists (C++: writes in order of mask bits 0-4)
        if (Quality == ItemQuality.Set && SetItemMask.HasValue)
        {
            int bonusIndex = 0;
            for (int i = 0; i < 5; i++)
            {
                if ((SetItemMask.Value & (1 << i)) != 0)
                {
                    if (bonusIndex < SetBonusStats.Count)
                        WriteStatList(ref writer, SetBonusStats[bonusIndex], externalData, saveVersion);
                    else
                        WriteStatList(ref writer, [], externalData, saveVersion);
                    bonusIndex++;
                }
            }
        }

        // Runeword stats (C++: always write if flag set)
        if (Flags.HasFlag(ItemFlags.Runeword))
        {
            WriteStatList(ref writer, RunewordStats ?? [], externalData, saveVersion);
        }

        // Chronicle data (v100+, IFLAG_CHRONICLE = bit 28)
        WriteChronicle(ref writer, saveVersion);

        // Advanced stash stack size (v100+)
        WriteAdvancedStashStackSize(ref writer, saveVersion);
    }

    /// <summary>
    /// Writes a stat list with paired stat handling.
    /// </summary>
    private static void WriteStatList(ref BitWriter writer, List<Stat> stats, IExternalData externalData, uint saveVersion)
    {
        // Build a set of paired follower stats so we don't write them individually
        var writtenStats = new HashSet<StatId>();
        var statMap = new Dictionary<StatId, Stat>(stats.Count);
        foreach (var stat in stats)
        {
            statMap[stat.Id] = stat;
        }

        foreach (var stat in stats)
        {
            // Skip if this stat was already written as part of a paired group
            if (writtenStats.Contains(stat.Id))
                continue;

            // C++: Explicitly skips STAT_POISON_COUNT - it's set on read but never written
            if (stat.Id == StatId.PoisonCount)
                continue;

            var statInfo = externalData.GetStatInfo(stat.Id, saveVersion);

            // C++: raw = (value >> ValShift) + SaveAdd
            // C++ skips stats where SaveBits == 0 or shifted value == 0: if (nSaveBits && nValue && ...)
            if (statInfo.SaveBits == 0)
                continue;

            long shiftedValue = stat.Value >> statInfo.ValShift;
            if (shiftedValue == 0)
                continue;

            // Write stat ID
            writer.WriteBits((uint)stat.Id, 9);

            // Write param if applicable
            if (statInfo.SaveParamBits > 0)
            {
                writer.WriteBits((uint)stat.Layer, statInfo.SaveParamBits);
            }

            // Write value
            writer.WriteBits((uint)(shiftedValue + statInfo.SaveAdd), statInfo.SaveBits);

            // Write paired stats that follow
            var pairedStatIds = stat.Id.GetPairedStats();
            foreach (StatId pairedStatId in pairedStatIds)
            {
                statMap.TryGetValue(pairedStatId, out var pairedStat);
                var pairedInfo = externalData.GetStatInfo(pairedStatId, saveVersion);

                writtenStats.Add(pairedStatId);

                // C++: raw = (value >> ValShift) + SaveAdd
                long pairedValue = pairedStat is not null ? pairedStat.Value : 0;
                long pairedShifted = pairedValue >> pairedInfo.ValShift;
                writer.WriteBits((uint)(pairedShifted + pairedInfo.SaveAdd), pairedInfo.SaveBits);
            }
        }

        // Write terminator
        writer.WriteBits((uint)StatId.Terminator, 9);
    }


    #region Format Helpers

    /// <summary>
    /// Reads the item format version using the appropriate encoding for the save version.
    /// Old format (saveVersion &lt;= 96): 10 bits
    /// New format (saveVersion > 96, server mode): 1 bit flag + 2 bits value
    /// </summary>
    private static ushort ReadItemFormatVersion(ref BitReader reader, uint saveVersion)
    {
        if (saveVersion > 96)
        {
            // New D2R server format: compact 3-bit encoding
            bool highFormat = reader.ReadBool();
            uint value = reader.ReadBits(2);
            return highFormat ? (ushort)(value + 99) : (ushort)value;
        }

        // Old format: 10 bits
        return (ushort)reader.ReadBits(10);
    }

    /// <summary>
    /// Writes the item format version using the appropriate encoding for the save version.
    /// Old format (saveVersion &lt;= 96): 10 bits
    /// New format (saveVersion > 96, server mode): 1 bit flag + 2 bits value
    /// </summary>
    private static void WriteItemFormat(ref BitWriter writer, uint version, uint saveVersion)
    {
        if (saveVersion > 96)
        {
            // New D2R server format: compact 3-bit encoding
            if (version >= 100)
            {
                writer.WriteBool(true);
                writer.WriteBits(Math.Min(version - 99, 3u), 2); // Can encode 100, 101, 102
            }
            else
            {
                writer.WriteBool(false);
                writer.WriteBits(Math.Min(version, 3u), 2); // Can encode 0, 1, 2, 3
            }
        }
        else
        {
            // Old format: 10 bits (clamped to 1023)
            writer.WriteBits(Math.Min(version, 1023u), 10);
        }
    }

    /// <summary>
    /// Reads the item code using the appropriate encoding for the save version.
    /// Old format: 32 bits raw
    /// New format: Huffman encoded
    /// </summary>
    private static uint ReadItemCode(ref BitReader reader, uint saveVersion)
    {
        if (saveVersion > 96)
            return HuffmanEncoding.Decode(ref reader);
        return reader.ReadUInt32();
    }

    /// <summary>
    /// Writes the item code using the appropriate encoding for the save version.
    /// Old format: 32 bits raw
    /// New format: Huffman encoded
    /// </summary>
    private static void WriteItemCode(ref BitWriter writer, uint itemCode, uint saveVersion)
    {
        if (saveVersion > 96)
            HuffmanEncoding.Encode(ref writer, itemCode);
        else
            writer.WriteUInt32(itemCode);
    }

    /// <summary>
    /// Reads realm data from the bit stream if present.
    /// </summary>
    private void ReadRealmData(ref BitReader reader, uint saveVersion)
    {
        if (saveVersion <= 86)
            return;

        if (!reader.ReadBool())
            return;

        var realmDataCount = saveVersion switch
        {
            > 96 => 4,
            > 93 => 3,
            _ => 2
        };

        RealmData = new uint[realmDataCount];
        for (int i = 0; i < realmDataCount; i++)
            RealmData[i] = reader.ReadUInt32();
    }

    /// <summary>
    /// Writes realm data to the bit stream.
    /// </summary>
    private void WriteRealmData(ref BitWriter writer, uint saveVersion)
    {
        if (saveVersion <= 86)
            return;

        bool hasRealmData = RealmData != null && RealmData.Length > 0 && RealmData.Any(v => v != 0);
        writer.WriteBool(hasRealmData);

        if (!hasRealmData)
            return;

        if (saveVersion > 96)
        {
            // New format: 4 x uint32
            if (RealmData!.Length < 4)
            {
                // Read from the old format: the game loads the two old dwords into [2] and [3]
                writer.WriteUInt32(0);
                writer.WriteUInt32(0);
                writer.WriteUInt32(RealmData.Length > 0 ? RealmData[0] : 0);
                writer.WriteUInt32(RealmData.Length > 1 ? RealmData[1] : 0);
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    writer.WriteUInt32(RealmData[i]);
            }
        }
        else
        {
            // Old format: 2 x uint32 (+ 1 discarded uint32 for v94-96)
            int offset = RealmData!.Length >= 4 ? 2 : 0;
            writer.WriteUInt32(RealmData[offset]);
            writer.WriteUInt32(RealmData.Length > offset + 1 ? RealmData[offset + 1] : 0);
            if (saveVersion > 93)
                writer.WriteUInt32(offset == 0 && RealmData.Length > 2 ? RealmData[2] : 0);
        }
    }

    /// <summary>
    /// Hardcoded item codes that have stacked item data in v100-101.
    /// </summary>
    private static readonly HashSet<uint> StackableItemCodes = BuildStackableItemCodes(
        "rvs rvl gcv gfv gsv gzv gpv gcy gfy gsy gly gpy gcb gfb gsb glb gpb gcg gfg gsg glg gpg gcr gfr gsr glr gpr gcw " +
        "gfw gsw glw gpw skc skf sku skl skz r01 r02 r03 r04 r05 r06 r07 r08 r09 r10 r11 r12 r13 r14 r15 r16 r17 r18 r19 " +
        "r20 r21 r22 r23 r24 r25 r26 r27 r28 r29 r30 r31 r32 r33 pk1 pk2 pk3 dhn bey mbr toa tes ceh bet fed ");

    /// <summary>
    /// Parses space-separated 3-char item codes into uint keys.
    /// Each code is 3 chars + 1 space = 4 bytes, matching D2's space-padded uint encoding.
    /// The input string must end with a trailing space so the last code is included.
    /// </summary>
    private static HashSet<uint> BuildStackableItemCodes(string codes)
    {
        var set = new HashSet<uint>();
        for (int i = 0; i + 3 < codes.Length; i += 4)
        {
            uint code = (uint)(codes[i] | (codes[i + 1] << 8) | (codes[i + 2] << 16) | (codes[i + 3] << 24));
            set.Add(code);
        }
        return set;
    }

    /// <summary>
    /// Reads advanced stash stack size (v100+, sub_140276B60).
    /// Controlled by AdvancedStashStackable column in items.txt.
    /// For v100-101: only for hardcoded stackable item types.
    /// For v102+: 1-bit flag + optional 8-bit value.
    /// </summary>
    private void ReadAdvancedStashStackSize(ref BitReader reader, uint saveVersion)
    {
        if (saveVersion <= 99) return;

        if (saveVersion <= 101)
        {
            // v100-101: only for items in the hardcoded stackable list
            if (!StackableItemCodes.Contains(ItemCode)) return;
        }
        else
        {
            // v102+: 1-bit presence flag
            if (!reader.ReadBool()) return;
        }

        AdvancedStashStackSize = (byte)reader.ReadBits(8);
    }

    /// <summary>
    /// Writes advanced stash stack size (v100+, sub_14027A650).
    /// </summary>
    private void WriteAdvancedStashStackSize(ref BitWriter writer, uint saveVersion)
    {
        if (saveVersion <= 99) return;

        if (saveVersion <= 101)
        {
            // v100-101: unconditionally present for hardcoded stackable items (no flag bit)
            if (!StackableItemCodes.Contains(ItemCode)) return;
            writer.WriteBits(AdvancedStashStackSize ?? 0, 8);
        }
        else
        {
            // v102+: 1-bit flag + optional 8-bit value
            writer.WriteBool(AdvancedStashStackSize.HasValue);
            if (AdvancedStashStackSize.HasValue)
                writer.WriteBits(AdvancedStashStackSize.Value, 8);
        }
    }

    /// <summary>
    /// Reads chronicle data (v100+, IFLAG_CHRONICLE = bit 28 = 0x10000000).
    /// Tracks when notable items (Unique/Set/Runeword) were found.
    /// Normal form: 16-bit chronicle_id, 32-bit timestamp, 4-bit count, count x 64-bit entries.
    /// Compact form (IFLAG_CHRONICLE_COMPACT = bit 29): 16-bit chronicle_id, 1 x 64-bit entry.
    /// </summary>
    private void ReadChronicle(ref BitReader reader, uint saveVersion)
    {
        if (saveVersion <= 99) return;
        if (!Flags.HasFlag(ItemFlags.Chronicle)) return;

        ChronicleIdBitOffset = reader.BitPosition;
        ChronicleId = (ushort)reader.ReadBits(16);

        bool isCompact = Flags.HasFlag(ItemFlags.ChronicleCompact);
        if (!isCompact)
        {
            ChronicleTimestamp = reader.ReadUInt32();
        }

        int count = isCompact ? 1 : (int)reader.ReadBits(4);
        if (count > 8) count = 8;
        if (count <= 0) return;

        ChronicleRecipients = new List<ulong>(count);
        for (int i = 0; i < count; i++)
            ChronicleRecipients.Add(reader.ReadUInt64());
    }

    /// <summary>
    /// Writes chronicle data (v100+, IFLAG_CHRONICLE = bit 28).
    /// </summary>
    private void WriteChronicle(ref BitWriter writer, uint saveVersion)
    {
        if (saveVersion <= 99) return;
        if (!Flags.HasFlag(ItemFlags.Chronicle)) return;

        writer.WriteBits(ChronicleId ?? 0, 16);

        bool isCompact = Flags.HasFlag(ItemFlags.ChronicleCompact);
        if (!isCompact)
        {
            writer.WriteUInt32(ChronicleTimestamp ?? 0);
        }

        int count;
        if (isCompact)
        {
            count = 1;
        }
        else
        {
            count = ChronicleRecipients?.Count ?? 0;
            count = Math.Clamp(count, 0, 8);
            writer.WriteBits((uint)count, 4);
        }

        if (ChronicleRecipients != null)
        {
            for (int i = 0; i < count && i < ChronicleRecipients.Count; i++)
                writer.WriteUInt64(ChronicleRecipients[i]);
        }
    }

    /// <summary>
    /// Reads a string using the appropriate encoding for the save version.
    /// Old format: 7-bit characters
    /// New format: 8-bit characters
    /// </summary>
    private static string ReadString(ref BitReader reader, uint saveVersion)
    {
        if (saveVersion > 97)
            return reader.Read8BitString();
        return reader.Read7BitString();
    }

    /// <summary>
    /// Writes a string using the appropriate encoding for the save version.
    /// Old format: 7-bit characters
    /// New format: 8-bit characters
    /// </summary>
    private static void WriteString(ref BitWriter writer, string str, uint saveVersion)
    {
        if (saveVersion > 97)
            writer.Write8BitString(str);
        else
            writer.Write7BitString(str);
    }

    #endregion
}
