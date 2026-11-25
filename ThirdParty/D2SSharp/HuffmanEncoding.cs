using D2SSharp.IO;

namespace D2SSharp;

/// <summary>
/// Huffman encoding/decoding for D2R item codes.
/// The Huffman tree is derived from the game binary (unk_141A8B510 table).
/// </summary>
public static class HuffmanEncoding
{
    // (symbol, bits, length) - bits stored MSB first
    private static readonly (char Symbol, uint Bits, int Length)[] Table =
    [
        ('0', 0b11111011, 8),
        (' ', 0b10, 2),
        ('1', 0b1111100, 7),
        ('2', 0b001100, 6),
        ('3', 0b1101101, 7),
        ('4', 0b11111010, 8),
        ('5', 0b00010110, 8),
        ('6', 0b1101111, 7),
        ('7', 0b01111, 5),
        ('8', 0b000100, 6),
        ('9', 0b01110, 5),
        ('a', 0b11110, 5),
        ('b', 0b0101, 4),
        ('c', 0b01000, 5),
        ('d', 0b110001, 6),
        ('e', 0b110000, 6),
        ('f', 0b010011, 6),
        ('g', 0b11010, 5),
        ('h', 0b00011, 5),
        ('i', 0b1111110, 7),
        ('j', 0b000101110, 9),
        ('k', 0b010010, 6),
        ('l', 0b11101, 5),
        ('m', 0b01101, 5),
        ('n', 0b001101, 6),
        ('o', 0b1111111, 7),
        ('p', 0b11001, 5),
        ('q', 0b11011001, 8),
        ('r', 0b11100, 5),
        ('s', 0b0010, 4),
        ('t', 0b01100, 5),
        ('u', 0b00001, 5),
        ('v', 0b1101110, 7),
        ('w', 0b00000, 5),
        ('x', 0b00111, 5),
        ('y', 0b0001010, 7),
        ('z', 0b11011000, 8)
    ];

    // Lookup table for encoding: indexed by character, returns (bits in LSB order, length)
    private static readonly (uint Bits, int Length)[] EncodeLookup;

    // Lookup tables for decoding: indexed by code length, maps MSB-first code to symbol
    private static readonly Dictionary<uint, char>[] DecodeLookup;

    // Maximum code length in the table
    private const int MaxCodeLength = 9;

    static HuffmanEncoding()
    {
        // Build encode lookup table (supports ASCII characters up to 127)
        EncodeLookup = new (uint Bits, int Length)[128];
        DecodeLookup = new Dictionary<uint, char>[MaxCodeLength + 1];
        for (int i = 0; i <= MaxCodeLength; i++)
        {
            DecodeLookup[i] = new Dictionary<uint, char>();
        }

        foreach (var (symbol, bits, length) in Table)
        {
            // Reverse bits from MSB-first to LSB-first for writing
            EncodeLookup[symbol] = (ReverseBits(bits, length), length);
            DecodeLookup[length][bits] = symbol;
        }
    }

    /// <summary>
    /// Reverses the bit order of a value within the specified bit length.
    /// </summary>
    private static uint ReverseBits(uint value, int length)
    {
        uint result = 0;
        for (int i = 0; i < length; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }

    /// <summary>
    /// Encodes an item code using Huffman encoding and writes to the bit stream.
    /// Item codes are 4 ASCII characters packed into an uint (little-endian).
    /// </summary>
    public static void Encode(ref BitWriter writer, uint itemCode)
    {
        // Extract and encode each character (little-endian: first char is the lowest byte)
        for (int i = 0; i < 4; i++)
        {
            char ch = (char)((itemCode >> (i * 8)) & 0xFF);
            if (ch == 0) break; // Stop at null terminator

            var (bits, length) = EncodeLookup[ch];
            if (length == 0)
                throw new ArgumentException($"Character '{ch}' (0x{(int)ch:X2}) not in Huffman table");

            writer.WriteBits(bits, length);
        }
    }

    /// <summary>
    /// Decodes an item code from the bit stream using Huffman decoding.
    /// Returns an uint containing 4 ASCII characters (little-endian).
    /// </summary>
    public static uint Decode(ref BitReader reader)
    {
        uint result = 0;

        // Decode 4 characters
        for (int charIndex = 0; charIndex < 4; charIndex++)
        {
            // Build up the code bit by bit (MSB first as stored in table)
            uint code = 0;
            int length = 0;
            char? decoded = null;

            while (length < MaxCodeLength)
            {
                uint bit = reader.ReadBits(1);
                // Shift code left and add new bit (building MSB first)
                code = (code << 1) | bit;
                length++;

                if (DecodeLookup[length].TryGetValue(code, out var symbol))
                {
                    decoded = symbol;
                    break;
                }
            }

            if (!decoded.HasValue)
                throw new InvalidOperationException($"Invalid Huffman code encountered at character {charIndex}");

            // Pack character into result (little-endian)
            result |= (uint)decoded.Value << (charIndex * 8);
        }

        return result;
    }
}
