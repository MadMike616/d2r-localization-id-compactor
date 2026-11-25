namespace D2SSharp.Enums;

/// <summary>
/// Demon type for bound demons.
/// Determines how ClassId is interpreted when restoring the demon.
/// </summary>
public enum DemonType : ushort
{
    /// <summary>Standard monster. ClassId is a direct MonStats.txt row index.</summary>
    Standard = 1,

    /// <summary>Super unique monster. ClassId is a SuperUniques.txt row index;
    /// the actual MonStats ID is looked up from the SuperUniques record at runtime.</summary>
    SuperUnique = 2
}
