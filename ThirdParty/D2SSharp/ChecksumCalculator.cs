namespace D2SSharp;

/// <summary>
/// Calculates the D2 save file checksum.
/// </summary>
public static class ChecksumCalculator
{
    /// <summary>
    /// Offset of the checksum field in the save file header.
    /// </summary>
    private const int ChecksumOffset = 0x0C;

    /// <summary>
    /// Calculates the checksum for a save file.
    /// The checksum field (at offset 0x0C) should be zeroed before calculation.
    /// </summary>
    public static uint Calculate(ReadOnlySpan<byte> data)
    {
        uint checksum = 0;

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];

            // Skip the checksum field itself (treat as zero)
            if (i >= ChecksumOffset && i < ChecksumOffset + 4)
            {
                b = 0;
            }

            // Rotate left by 1 and add the byte
            checksum = ((checksum << 1) | (checksum >> 31)) + b;
        }

        return checksum;
    }

    /// <summary>
    /// Verifies the checksum of a save file.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> data)
    {
        if (data.Length < ChecksumOffset + 4)
            return false;

        uint stored = BitConverter.ToUInt32(data.Slice(ChecksumOffset, 4));
        uint calculated = Calculate(data);

        return stored == calculated;
    }

    /// <summary>
    /// Updates the checksum in a save file buffer.
    /// </summary>
    public static void Update(Span<byte> data)
    {
        if (data.Length < ChecksumOffset + 4)
            throw new ArgumentException("Data too short for checksum", nameof(data));

        // Zero the checksum field first
        data.Slice(ChecksumOffset, 4).Clear();

        // Calculate and write the checksum
        uint checksum = Calculate(data);
        BitConverter.TryWriteBytes(data.Slice(ChecksumOffset, 4), checksum);
    }
}
