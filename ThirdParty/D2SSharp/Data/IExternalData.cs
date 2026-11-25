using D2SSharp.Enums;

namespace D2SSharp.Data;

/// <summary>
/// Interface for providing external game data required for parsing.
/// Implement this interface with data from ItemStatCost.txt, Items.txt, ItemTypes.txt, etc.
/// </summary>
public interface IExternalData
{
    /// <summary>
    /// Gets stat information for a given stat ID.
    /// </summary>
    /// <param name="statId">The stat ID (0-510, 511 is terminator)</param>
    /// <param name="saveVersion">Save file version (different versions may have different stat definitions)</param>
    /// <returns>Stat information, or null if stat ID is unknown</returns>
    StatInfo GetStatInfo(StatId statId, uint saveVersion);

    /// <summary>
    /// Gets the item file index for a given item code.
    /// Matches C++ DATATBLS_GetItemRecordFromCode behavior.
    /// </summary>
    /// <param name="itemCode">4-character item code as uint (e.g., 'cap ' = 0x20706163)</param>
    /// <param name="saveVersion">Save file version (different versions may have different item tables)</param>
    /// <returns>Item file index (row in Items.txt), or -1 if unknown</returns>
    int GetItemIndex(uint itemCode, uint saveVersion);

    /// <summary>
    /// Gets item type information for a given item file index.
    /// </summary>
    /// <param name="itemIndex">Item file index (row in Items.txt)</param>
    /// <param name="saveVersion">Save file version (different versions may have different item definitions)</param>
    /// <returns>Item type info, or null if unknown</returns>
    ItemTypeInfo GetItemInfo(int itemIndex, uint saveVersion);

    /// <summary>
    /// Gets the bit width for quantity values.
    /// This is version-dependent (9 bits for version > 80, 8 bits otherwise).
    /// Not from ItemStatCost.txt - hardcoded in game code.
    /// </summary>
    /// <param name="saveVersion">Save file version</param>
    int GetQuantityBits(uint saveVersion) => saveVersion > 80 ? 9 : 8;

    /// <summary>
    /// Gets the magic prefix ID offset (nMagicPrefixOffset).
    /// Used for adjusting magic prefix IDs during serialization.
    /// </summary>
    /// <param name="saveVersion">Save file version</param>
    int GetMagicPrefixOffset(uint saveVersion)
    {
        switch (saveVersion)
        {
            case 0x48u:
            case 0x49u:
            case 0x50u:
            case 0x51u:
            case 0x52u:
                return -661;
            case 0x53u:
            case 0x54u:
                return -674;
            case 0x55u:
            case 0x56u:
            case 0x57u:
                return -675;
            case 0x58u:
                return -729;
        }

        return 0;
    }

    /// <summary>
    /// Gets the auto magic affix ID offset (nAutoMagicOffset).
    /// Used for adjusting auto affix IDs during serialization.
    /// </summary>
    /// <param name="saveVersion">Save file version</param>
    int GetAutoMagicOffset(uint saveVersion)
    {
        switch (saveVersion)
        {
            case 0x48u:
            case 0x49u:
            case 0x50u:
            case 0x51u:
            case 0x52u:
                return -1262;
            case 0x53u:
            case 0x54u:
                return -1265;
            case 0x55u:
            case 0x56u:
            case 0x57u:
                return -1266;
            case 0x58u:
                return -1440;
        }
        return 0;
    }
}

/// <summary>
/// Information about a stat from ItemStatCost.txt
/// </summary>
/// <param name="CsvBits">Number of bits for player stat value (nCsvBits)</param>
/// <param name="CsvParam">Number of bits for player stat parameter (nCsvParam)</param>
/// <param name="CsvSigned">Whether CSV value is signed (dwItemStatFlags bit 13)</param>
/// <param name="SaveBits">Number of bits for item stat value (nSaveBits)</param>
/// <param name="SaveParamBits">Number of bits for item stat parameter (dwSaveParamBits)</param>
/// <param name="SaveAdd">Offset added when saving (dwSaveAdd)</param>
/// <param name="ValShift">Bit shift applied to value for fixed-point precision (nValShift).
/// C++ shifts left on read: (raw - SaveAdd) &lt;&lt; ValShift.
/// C++ shifts right on write: value >> ValShift + SaveAdd.</param>
public readonly record struct StatInfo(
    byte CsvBits,
    byte CsvParam,
    bool CsvSigned,
    int SaveBits,
    int SaveParamBits,
    int SaveAdd,
    byte ValShift
)
{
    /// <summary>
    /// Maximum semantic value that can be stored for this stat.
    /// Calculated as: (1 &lt;&lt; SaveBits) - 1 - SaveAdd
    /// </summary>
    public int MaxSemanticValue => (1 << SaveBits) - 1 - SaveAdd;
}

/// <summary>
/// Information about an item type from Items.txt and ItemTypes.txt
/// </summary>
/// <param name="CompactSave">Whether item uses compact save format (nCompactSave)</param>
/// <param name="HasVariableGfx">Whether item has variable inventory graphics</param>
/// <param name="IsArmor">Whether item is armor type (ITEMTYPE_ANY_ARMOR)</param>
/// <param name="IsWeapon">Whether item is weapon type (ITEMTYPE_WEAPON)</param>
/// <param name="IsGold">Whether item is gold type (ITEMTYPE_GOLD)</param>
/// <param name="IsStackable">Whether item is stackable (nStackable)</param>
/// <param name="IsCharm">Whether item is a charm type (ITEMTYPE_CHARM)</param>
/// <param name="IsBodyPart">Whether item is a monster body part (ITEMTYPE_BODY_PART)</param>
/// <param name="IsPlayerBodyPart">Whether item is a player body part (ITEMTYPE_PLAYER_BODY_PART)</param>
/// <param name="IsScrollOrBook">Whether item is a scroll or book</param>
/// <param name="IsQuest">Whether item is a quest item (nQuest)</param>
/// <param name="QuestDiffCheck">Whether quest item has difficulty check (nQuestDiffCheck)</param>
public readonly record struct ItemTypeInfo(
    bool CompactSave,
    bool HasVariableGfx,
    bool IsArmor,
    bool IsWeapon,
    bool IsGold,
    bool IsStackable,
    bool IsCharm,
    bool IsBodyPart,
    bool IsPlayerBodyPart,
    bool IsScrollOrBook,
    bool IsQuest,
    bool QuestDiffCheck
);
