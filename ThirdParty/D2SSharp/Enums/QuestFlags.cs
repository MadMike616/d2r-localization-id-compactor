namespace D2SSharp.Enums;

/// <summary>
/// Quest state flags stored per quest per difficulty.
/// Multiple flags can be set simultaneously to track quest progress.
/// </summary>
[Flags]
public enum QuestFlags : ushort
{
    None = 0,
    RewardGranted = 0x1,
    RewardPending = 0x2,
    Started = 0x4,
    LeftTown = 0x8,
    EnterArea = 0x10,
    Custom1 = 0x20,
    Custom2 = 0x40,
    Custom3 = 0x80,
    Custom4 = 0x100,
    Custom5 = 0x200,
    Custom6 = 0x400,
    Custom7 = 0x800,
    QuestLog = 0x1000,
    PrimaryGoalAchieved = 0x2000,
    CompletedNow = 0x4000,
    CompletedBefore = 0x8000,
}
