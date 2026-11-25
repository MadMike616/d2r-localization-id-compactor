namespace D2SSharp.Enums;

/// <summary>
/// Stash tab type (StashFormat >= 2).
/// Determines what the tab body contains and which game version can access it.
/// </summary>
public enum StashTabType : byte
{
    /// <summary>Normal stash tab. Body contains items (JM section).</summary>
    Normal = 0,

    /// <summary>Advanced stash tab with stackable item support. Body contains items (JM section).</summary>
    AdvancedStash = 1,

    /// <summary>Chronicle tab. Body contains a chronicle section (magic 0xC0EAEDC0) tracking found set/unique/runeword items.</summary>
    Chronicle = 2
}
