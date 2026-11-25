using D2SSharp.Enums;

namespace D2SSharp.Model;


/// <summary>
/// A single stat (used for both player stats and item stats).
/// </summary>
public partial record Stat
{
    /// <summary>Stat ID from ItemStatCost.txt</summary>
    public StatId Id { get; set; }

    /// <summary>Layer/parameter (skill ID, etc.). Defaults to 0.</summary>
    public int Layer { get; set; } = 0;

    /// <summary>Stat value</summary>
    public long Value { get; set; }
}
