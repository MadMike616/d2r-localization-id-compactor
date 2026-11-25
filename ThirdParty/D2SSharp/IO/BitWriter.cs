using System.Text;

namespace D2SSharp.IO;

/// <summary>
/// A ref struct for writing bits to a byte span with minimal allocations.
/// Bits are written LSB-first within each byte (little-endian bit order).
/// </summary>
public ref struct BitWriter
{
    private readonly Span<byte> _data;
    private int _bitPosition;

    public BitWriter(Span<byte> data)
    {
        _data = data;
        _bitPosition = 0;
        // Clear the buffer
        data.Clear();
    }

    /// <summary>Current bit position in the stream.</summary>
    public readonly int BitPosition => _bitPosition;

    /// <summary>Current byte position (bit position / 8).</summary>
    public readonly int BytePosition => _bitPosition >> 3;

    /// <summary>Number of bytes written (rounded up).</summary>
    public readonly int BytesWritten => (_bitPosition + 7) >> 3;

    /// <summary>
    /// Writes up to 32 bits from an unsigned value.
    /// </summary>
    public void WriteBits(uint value, int count)
    {
        if (count == 0) return;
        if (count > 32) throw new ArgumentOutOfRangeException(nameof(count), "Cannot write more than 32 bits at once");

        int bitsWritten = 0;

        while (bitsWritten < count)
        {
            int byteIndex = _bitPosition >> 3;
            int bitOffset = _bitPosition & 7;

            if (byteIndex >= _data.Length)
                throw new InvalidOperationException("Buffer overflow: attempted to write past end of data");

            // How many bits can we write to the current byte?
            int bitsAvailable = 8 - bitOffset;
            int bitsToWrite = Math.Min(bitsAvailable, count - bitsWritten);

            // Extract the bits to write from the value
            uint mask = (1u << bitsToWrite) - 1;
            uint bits = (value >> bitsWritten) & mask;

            // Write to the current byte
            _data[byteIndex] |= (byte)(bits << bitOffset);

            _bitPosition += bitsToWrite;
            bitsWritten += bitsToWrite;
        }
    }

    /// <summary>
    /// Writes up to 32 bits from a signed value.
    /// </summary>
    public void WriteSignedBits(int value, int count)
    {
        WriteBits((uint)value, count);
    }

    /// <summary>
    /// Writes a single bit from a boolean.
    /// </summary>
    public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

    /// <summary>
    /// Writes a byte (8 bits).
    /// </summary>
    public void WriteByte(byte value) => WriteBits(value, 8);

    /// <summary>
    /// Writes a 16-bit unsigned integer.
    /// </summary>
    public void WriteUInt16(ushort value) => WriteBits(value, 16);

    /// <summary>
    /// Writes a 32-bit unsigned integer.
    /// </summary>
    public void WriteUInt32(uint value) => WriteBits(value, 32);

    /// <summary>
    /// Writes a 64-bit unsigned integer.
    /// </summary>
    public void WriteUInt64(ulong value)
    {
        WriteBits((uint)value, 32);
        WriteBits((uint)(value >> 32), 32);
    }

    /// <summary>
    /// Writes a 16-bit signed integer.
    /// </summary>
    public void WriteInt16(short value) => WriteBits((uint)value, 16);

    /// <summary>
    /// Writes a 32-bit signed integer.
    /// </summary>
    public void WriteInt32(int value) => WriteBits((uint)value, 32);

    /// <summary>
    /// Writes bytes from a span. Must be byte-aligned.
    /// </summary>
    public void WriteBytes(scoped ReadOnlySpan<byte> bytes)
    {
        if ((_bitPosition & 7) != 0)
            throw new InvalidOperationException("WriteBytes requires byte alignment");

        int bytePos = _bitPosition >> 3;
        if (bytePos + bytes.Length > _data.Length)
            throw new InvalidOperationException("Buffer overflow: attempted to write past end of data");

        bytes.CopyTo(_data[bytePos..]);
        _bitPosition += bytes.Length << 3;
    }

    /// <summary>
    /// Aligns the writer to the next byte boundary by writing zero bits.
    /// </summary>
    public void AlignToByte()
    {
        int remainder = _bitPosition & 7;
        if (remainder != 0)
        {
            _bitPosition += 8 - remainder;
        }
    }

    /// <summary>
    /// Skips the specified number of bits (leaves as zeros).
    /// </summary>
    public void SkipBits(int count)
    {
        _bitPosition += count;
    }

    /// <summary>
    /// Skips the specified number of bytes (leaves as zeros). Must be byte-aligned.
    /// </summary>
    public void SkipBytes(int count)
    {
        if ((_bitPosition & 7) != 0)
            throw new InvalidOperationException("SkipBytes requires byte alignment");
        _bitPosition += count << 3;
    }

    /// <summary>
    /// Writes a null-terminated string with UTF-8 encoding.
    /// </summary>
    public void Write8BitString(ReadOnlySpan<char> str)
    {
        Span<byte> buffer = stackalloc byte[64 * 3]; // Max 3 bytes per char for BMP
        int byteCount = Encoding.UTF8.GetBytes(str, buffer);
        for (int i = 0; i < byteCount; i++)
        {
            WriteBits(buffer[i], 8);
        }
        WriteBits(0, 8); // Null terminator
    }

    /// <summary>
    /// Writes a null-terminated string with 7-bit character encoding.
    /// </summary>
    public void Write7BitString(ReadOnlySpan<char> str)
    {
        foreach (char ch in str)
        {
            WriteBits((uint)ch & 0x7F, 7);
        }
        WriteBits(0, 7); // Null terminator
    }

    /// <summary>
    /// Writes a fixed-length UTF-8 string, padding with nulls.
    /// </summary>
    public void WriteFixedString(ReadOnlySpan<char> str, int byteLength)
    {
        if ((_bitPosition & 7) != 0)
            throw new InvalidOperationException("WriteFixedString requires byte alignment");

        int bytePos = _bitPosition >> 3;
        if (bytePos + byteLength > _data.Length)
            throw new InvalidOperationException("Buffer overflow");

        var dest = _data.Slice(bytePos, byteLength);
        dest.Clear();

        // Encode string as UTF-8 (matching ReadFixedString which decodes UTF-8)
        Encoding.UTF8.GetBytes(str, dest);

        _bitPosition += byteLength << 3;
    }

    /// <summary>
    /// Gets a span of the written data.
    /// </summary>
    public readonly Span<byte> GetWrittenSpan()
    {
        return _data[..BytesWritten];
    }

    /// <summary>
    /// Sets the bit position.
    /// </summary>
    public void SetBitPosition(int position)
    {
        _bitPosition = position;
    }

    /// <summary>
    /// Sets the byte position (bit position = bytePosition * 8).
    /// </summary>
    public void SetBytePosition(int position)
    {
        _bitPosition = position << 3;
    }

    /// <summary>
    /// Gets a span at the specified byte offset for direct writing.
    /// </summary>
    public readonly Span<byte> GetSpanAt(int byteOffset, int length)
    {
        return _data.Slice(byteOffset, length);
    }
}
