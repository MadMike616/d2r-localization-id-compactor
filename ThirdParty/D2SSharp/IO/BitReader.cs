using System.Text;

namespace D2SSharp.IO;

/// <summary>
/// A ref struct for reading bits from a byte span with minimal allocations.
/// Bits are read LSB-first within each byte (little-endian bit order).
/// </summary>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bitPosition = 0;
    }

    /// <summary>Current bit position in the stream.</summary>
    public readonly int BitPosition => _bitPosition;

    /// <summary>Current byte position (bit position / 8).</summary>
    public readonly int BytePosition => _bitPosition >> 3;

    /// <summary>Number of bits remaining in the stream.</summary>
    public readonly int BitsRemaining => (_data.Length << 3) - _bitPosition;

    /// <summary>
    /// Reads up to 32 bits as an unsigned value.
    /// </summary>
    public uint ReadBits(int count)
    {
        if (count == 0) return 0;
        if (count > 32) throw new ArgumentOutOfRangeException(nameof(count), "Cannot read more than 32 bits at once");

        uint result = 0;
        int bitsRead = 0;

        while (bitsRead < count)
        {
            int byteIndex = _bitPosition >> 3;
            int bitOffset = _bitPosition & 7;

            if (byteIndex >= _data.Length)
                throw new EndOfStreamException("Attempted to read past end of data");

            // How many bits can we read from the current byte?
            int bitsAvailable = 8 - bitOffset;
            int bitsToRead = Math.Min(bitsAvailable, count - bitsRead);

            // Extract bits from current byte
            uint mask = (1u << bitsToRead) - 1;
            uint bits = (uint)((_data[byteIndex] >> bitOffset) & mask);

            // Add to result at the appropriate position
            result |= bits << bitsRead;

            _bitPosition += bitsToRead;
            bitsRead += bitsToRead;
        }

        return result;
    }

    /// <summary>
    /// Reads up to 32 bits as a signed value using sign extension.
    /// </summary>
    public int ReadSignedBits(int count)
    {
        if (count == 0) return 0;
        if (count > 32) throw new ArgumentOutOfRangeException(nameof(count), "Cannot read more than 32 bits at once");

        uint raw = ReadBits(count);

        // Sign extend if the high bit is set
        if (count < 32 && (raw & (1u << (count - 1))) != 0)
        {
            // Set all bits above 'count' to 1
            raw |= ~((1u << count) - 1);
        }

        return (int)raw;
    }

    /// <summary>
    /// Reads a single bit as a boolean.
    /// </summary>
    public bool ReadBool() => ReadBits(1) != 0;

    /// <summary>
    /// Reads a byte (8 bits).
    /// </summary>
    public byte ReadByte() => (byte)ReadBits(8);

    /// <summary>
    /// Reads a 16-bit unsigned integer.
    /// </summary>
    public ushort ReadUInt16() => (ushort)ReadBits(16);

    /// <summary>
    /// Reads a 32-bit unsigned integer.
    /// </summary>
    public uint ReadUInt32() => ReadBits(32);

    /// <summary>
    /// Reads a 64-bit unsigned integer.
    /// </summary>
    public ulong ReadUInt64()
    {
        uint low = ReadBits(32);
        uint high = ReadBits(32);
        return (ulong)high << 32 | low;
    }

    /// <summary>
    /// Reads a 16-bit signed integer.
    /// </summary>
    public short ReadInt16() => (short)ReadBits(16);

    /// <summary>
    /// Reads a 32-bit signed integer.
    /// </summary>
    public int ReadInt32() => (int)ReadBits(32);

    /// <summary>
    /// Reads the specified number of bytes. Must be byte-aligned.
    /// </summary>
    private ReadOnlySpan<byte> ReadBytes(int count)
    {
        if ((_bitPosition & 7) != 0)
            throw new InvalidOperationException("ReadBytes requires byte alignment");

        int bytePos = _bitPosition >> 3;
        if (bytePos + count > _data.Length)
            throw new EndOfStreamException("Attempted to read past end of data");

        var result = _data.Slice(bytePos, count);
        _bitPosition += count << 3;
        return result;
    }

    /// <summary>
    /// Reads bytes into the provided span. Must be byte-aligned.
    /// </summary>
    public void ReadBytes(scoped Span<byte> destination)
    {
        ReadBytes(destination.Length).CopyTo(destination);
    }

    /// <summary>
    /// Aligns the reader to the next byte boundary.
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
    /// Skips the specified number of bits.
    /// </summary>
    public void SkipBits(int count)
    {
        _bitPosition += count;
    }

    /// <summary>
    /// Skips the specified number of bytes. Must be byte-aligned.
    /// </summary>
    public void SkipBytes(int count)
    {
        if ((_bitPosition & 7) != 0)
            throw new InvalidOperationException("SkipBytes requires byte alignment");
        _bitPosition += count << 3;
    }

    /// <summary>
    /// Reads a null-terminated string with 8-bit character encoding.
    /// </summary>
    public string Read8BitString()
    {
        Span<byte> buffer = stackalloc byte[64];
        int length = 0;

        while (length < buffer.Length)
        {
            uint ch = ReadBits(8);
            if (ch == 0) break;
            buffer[length++] = (byte)ch;
        }

        return Encoding.UTF8.GetString(buffer[..length]);
    }

    /// <summary>
    /// Reads a null-terminated string with 7-bit character encoding.
    /// </summary>
    public string Read7BitString()
    {
        Span<byte> buffer = stackalloc byte[32];
        int length = 0;

        while (length < buffer.Length)
        {
            uint ch = ReadBits(7);
            if (ch == 0) break;
            buffer[length++] = (byte)ch;
        }

        return Encoding.ASCII.GetString(buffer[..length]);
    }

    /// <summary>
    /// Reads a fixed-length ASCII string, stopping at null terminator.
    /// </summary>
    public string ReadFixedString(int byteLength)
    {
        var bytes = ReadBytes(byteLength);
        int length = bytes.IndexOf((byte)0);
        if (length < 0) length = byteLength;

        return Encoding.UTF8.GetString(bytes[..length]);
    }

    /// <summary>
    /// Peeks at bits without advancing the position.
    /// </summary>
    public readonly uint PeekBits(int count)
    {
        var copy = this;
        return copy.ReadBits(count);
    }

    /// <summary>
    /// Gets a slice of the underlying data starting at the current byte position.
    /// </summary>
    public readonly ReadOnlySpan<byte> GetRemainingBytes()
    {
        int bytePos = (_bitPosition + 7) >> 3;
        return _data[bytePos..];
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
}
