namespace D2SSharp.Data;

/// <summary>
/// Shared item type codes used for item classification.
/// 4-character codes stored as uint (little-endian).
/// </summary>
public static class ItemTypeCodes
{
    public static readonly uint Gold = MakeCode("gold");
    public static readonly uint Armor = MakeCode("armo");
    public static readonly uint Weapon = MakeCode("weap");
    public static readonly uint Charm = MakeCode("char");
    public static readonly uint BodyPart = MakeCode("body");
    public static readonly uint PlayerBodyPart = MakeCode("play");
    public static readonly uint Scroll = MakeCode("scro");
    public static readonly uint Book = MakeCode("book");

    /// <summary>
    /// Converts a 4-character item code string to a uint (little-endian).
    /// Pads shorter codes with spaces to 4 characters.
    /// </summary>
    public static uint MakeCode(string code)
    {
        if (code.Length > 4) throw new ArgumentException("Code must be 4 characters or less");
        code = code.PadRight(4);
        uint result = 0;
        for (int i = 0; i < 4; i++)
            result |= (uint)code[i] << (i * 8);
        return result;
    }
}
