namespace D2SSharp.Enums;

/// <summary>
/// Character state flags stored in the save file header.
/// Includes game mode, progression, and status information.
/// </summary>
[Flags]
public enum CharacterFlags : uint
{
    None = 0x0,
    Init = 0x1,
    RealmChar = 0x2,
    Hardcore = 0x4,
    Dead = 0x8,
    SaveProcess = 0x10,
    Expansion = 0x20,
    Ladder = 0x40,
    NeedsRenaming = 0x80,
    WeaponSwitch = 0x2000,
}

/// <summary>
/// Extension methods for extracting progression data from character flags.
/// </summary>
public static class CharacterFlagsExtensions
{
    private const uint ProgressionMask = 0x1F00;
    private const int ProgressionShift = 8;

    extension(CharacterFlags flags)
    {
        /// <summary>
        /// Gets the character's progression value (0-15 for expansion, 0-12 for classic).
        /// Each act completion increments this value.
        /// </summary>
        public uint Progression => ((uint)flags & ProgressionMask) >> ProgressionShift;

        /// <summary>
        /// Gets the maximum difficulty the character can access based on progression.
        /// </summary>
        public Difficulty MaxAllowedDifficulty
        {
            get
            {
                // Extract progression value (0–15 for expansion, 0–12 for classic)
                uint progression = flags.Progression;

                uint actsPerDifficulty = flags.HasFlag(CharacterFlags.Expansion) ? 5u : 4u;

                // 0 = Normal Allowed
                // 1 = Nightmare Allowed / Normal completed
                // 2 = Hell Allowed / Nightmare completed
                // 3 = Hell Completed
                uint difficultyAllowed = progression / actsPerDifficulty;
                if (difficultyAllowed > 2) difficultyAllowed = 2;
                return (Difficulty)difficultyAllowed;
            }
        }
    }
}
