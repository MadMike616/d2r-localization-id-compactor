namespace D2SSharp.Model;

/// <summary>
/// NPC dialog flags for a single NPC (quest intro + NPC intro).
/// </summary>
public partial class NpcDialogData
{
    /// <summary>Whether the quest introduction has been shown for this NPC.</summary>
    public bool QuestIntroShown { get; set; }

    /// <summary>Whether the NPC introduction/congratulations has been shown.</summary>
    public bool NpcIntroShown { get; set; }
}

/// <summary>
/// NPC dialog flags for a single difficulty (2 × 8 bytes).
/// Based on C++ npcIndexMap: indices 0-34 for various NPCs.
/// </summary>
public partial class NpcDialogDifficulty
{
    /// <summary>Quest intro flags (8 bytes = 64 bits).</summary>
    public byte[] QuestIntroFlags { get; set; } = new byte[8];

    /// <summary>NPC intro flags (8 bytes = 64 bits).</summary>
    public byte[] NpcIntroFlags { get; set; } = new byte[8];

    // Index 0 is unused/reserved
    public NpcDialogData Gheed => GetNpc(1);
    public NpcDialogData Akara => GetNpc(2);
    public NpcDialogData Kashya => GetNpc(3);
    public NpcDialogData WarrivActI => GetNpc(4);
    public NpcDialogData Charsi => GetNpc(5);
    public NpcDialogData CainActI => GetNpc(6);
    public NpcDialogData WarrivActII => GetNpc(7);
    public NpcDialogData Atma => GetNpc(8);
    public NpcDialogData Drognan => GetNpc(9);
    public NpcDialogData Fara => GetNpc(10);
    public NpcDialogData Lysander => GetNpc(11);
    public NpcDialogData Geglash => GetNpc(12);
    public NpcDialogData MeshifActII => GetNpc(13);
    public NpcDialogData Jerhyn => GetNpc(14);
    public NpcDialogData Greiz => GetNpc(15);
    public NpcDialogData Elzix => GetNpc(16);
    public NpcDialogData CainActII => GetNpc(17);
    public NpcDialogData CainActIII => GetNpc(18);
    public NpcDialogData CainActIV => GetNpc(19);
    public NpcDialogData Tyrael => GetNpc(20);
    public NpcDialogData Asheara => GetNpc(21);
    public NpcDialogData Hratli => GetNpc(22);
    public NpcDialogData Alkor => GetNpc(23);
    public NpcDialogData Ormus => GetNpc(24);
    public NpcDialogData Izual => GetNpc(25);
    public NpcDialogData Halbu => GetNpc(26);
    public NpcDialogData MeshifActIII => GetNpc(27);
    public NpcDialogData Natalya => GetNpc(28);
    public NpcDialogData Larzuk => GetNpc(29);
    public NpcDialogData Anya => GetNpc(30);
    public NpcDialogData Malah => GetNpc(31);
    public NpcDialogData Nihlathak => GetNpc(32);
    public NpcDialogData Qualkehk => GetNpc(33);
    public NpcDialogData CainActV => GetNpc(34);

    /// <summary>
    /// Gets dialog data for an NPC by index.
    /// </summary>
    public NpcDialogData GetNpc(int index)
    {
        return new NpcDialogData
        {
            QuestIntroShown = GetBit(QuestIntroFlags, index),
            NpcIntroShown = GetBit(NpcIntroFlags, index)
        };
    }

    /// <summary>
    /// Sets dialog data for an NPC by index.
    /// </summary>
    public void SetNpc(int index, NpcDialogData data)
    {
        SetBit(QuestIntroFlags, index, data.QuestIntroShown);
        SetBit(NpcIntroFlags, index, data.NpcIntroShown);
    }

    /// <summary>
    /// Gets whether quest intro was shown for an NPC.
    /// </summary>
    public bool IsQuestIntroShown(int index) => GetBit(QuestIntroFlags, index);

    /// <summary>
    /// Sets whether quest intro was shown for an NPC.
    /// </summary>
    public void SetQuestIntroShown(int index, bool value) => SetBit(QuestIntroFlags, index, value);

    /// <summary>
    /// Gets whether NPC intro was shown for an NPC.
    /// </summary>
    public bool IsNpcIntroShown(int index) => GetBit(NpcIntroFlags, index);

    /// <summary>
    /// Sets whether NPC intro was shown for an NPC.
    /// </summary>
    public void SetNpcIntroShown(int index, bool value) => SetBit(NpcIntroFlags, index, value);

    private static bool GetBit(byte[] buffer, int index)
    {
        if (index < 0 || index >= 64) return false;
        int byteIndex = index / 8;
        int bitIndex = index % 8;
        return (buffer[byteIndex] & (1 << bitIndex)) != 0;
    }

    private static void SetBit(byte[] buffer, int index, bool value)
    {
        if (index < 0 || index >= 64) return;
        int byteIndex = index / 8;
        int bitIndex = index % 8;

        if (value)
            buffer[byteIndex] |= (byte)(1 << bitIndex);
        else
            buffer[byteIndex] &= (byte)~(1 << bitIndex);
    }
}
