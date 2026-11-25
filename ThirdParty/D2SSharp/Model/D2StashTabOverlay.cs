using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using D2SSharp.Enums;

namespace D2SSharp.Model;

/// <summary>
/// Blittable layout for the fixed 64-byte header of a shared stash tab.
/// Provides zero-copy access to tab header fields without parsing items.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public unsafe struct D2StashTabLayout
{
    /// <summary>Size of the stash tab header in bytes.</summary>
    public const int Size = 64;

    [FieldOffset(0)] public uint Magic;
    [FieldOffset(4)] public uint StashFormat;
    [FieldOffset(8)] public uint SaveVersion;
    [FieldOffset(12)] public uint Gold;
    [FieldOffset(16)] public ushort TabSize;
    [FieldOffset(18)] public ushort Season;
    [FieldOffset(20)] public StashTabType TabType;
    [FieldOffset(21)] private fixed byte _padding21[3];
    [FieldOffset(24)] private fixed byte _withdrawOnlyData[20];
    [FieldOffset(44)] private fixed byte _padding44[20];

    /// <summary>Opaque withdraw-only tab data (20 bytes). See <see cref="D2StashTab.WithdrawOnlyData"/>.</summary>
    public Span<byte> WithdrawOnlyData => MemoryMarshal.CreateSpan(ref _withdrawOnlyData[0], 20);

    /// <summary>Whether the tab is withdraw-only (items can be taken out but not put in).</summary>
    public readonly bool IsWithdrawOnly =>
        MemoryMarshal.Read<uint>(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in _withdrawOnlyData[0]), 4)) != 0;

    private readonly bool IsValid => Magic == D2StashTab.Magic;

    /// <summary>
    /// Gets a mutable reference to the layout from a byte span.
    /// </summary>
    public static ref D2StashTabLayout From(Span<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException($"Data too small for stash tab header: {data.Length} bytes, need {Size}.");
        ref var layout = ref MemoryMarshal.AsRef<D2StashTabLayout>(data);
        if (!layout.IsValid)
            throw new InvalidDataException($"Invalid stash tab magic: 0x{layout.Magic:X8}, expected 0x{D2StashTab.Magic:X8}");
        return ref layout;
    }

    /// <summary>
    /// Gets a read-only reference to the layout from a read-only byte span.
    /// </summary>
    public static ref readonly D2StashTabLayout FromReadOnly(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException($"Data too small for stash tab header: {data.Length} bytes, need {Size}.");
        ref readonly var layout = ref MemoryMarshal.AsRef<D2StashTabLayout>(data);
        if (!layout.IsValid)
            throw new InvalidDataException($"Invalid stash tab magic: 0x{layout.Magic:X8}, expected 0x{D2StashTab.Magic:X8}");
        return ref layout;
    }
}
