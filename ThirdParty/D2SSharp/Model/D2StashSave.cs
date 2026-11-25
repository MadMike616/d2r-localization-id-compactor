using D2SSharp.Data;
using D2SSharp.IO;

namespace D2SSharp.Model;

/// <summary>
/// Diablo 2 Resurrected shared stash save file - extends <see cref="List{T}"/> for direct tab access.
/// </summary>
public partial class D2StashSave : List<D2StashTab>
{
    /// <summary>
    /// Reads all stash tabs from a BitReader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <param name="externalData">External game data for parsing items.</param>
    /// <returns>The parsed stash save containing all tabs.</returns>
    private static D2StashSave Read(ref BitReader reader, IExternalData externalData)
    {
        var save = new D2StashSave();

        while (reader.BitsRemaining > D2StashTab.HeaderSize * 8)
        {
            save.Add(D2StashTab.Read(ref reader, externalData));
        }

        return save;
    }

    /// <summary>
    /// Reads all stash tabs from a byte span using the default external data.
    /// </summary>
    /// <param name="data">The stash file data.</param>
    /// <returns>The parsed stash save containing all tabs.</returns>
    public static D2StashSave Read(ReadOnlySpan<byte> data) => Read(data, TxtFileExternalData.Default);

    /// <summary>
    /// Reads all stash tabs from a byte span.
    /// </summary>
    /// <param name="data">The stash file data.</param>
    /// <param name="externalData">External game data for parsing items.</param>
    /// <returns>The parsed stash save containing all tabs.</returns>
    public static D2StashSave Read(ReadOnlySpan<byte> data, IExternalData externalData)
    {
        var reader = new BitReader(data);
        return Read(ref reader, externalData);
    }

    /// <summary>
    /// Calculates an estimated buffer size for writing all tabs.
    /// Returns a conservative estimate that should be large enough.
    /// </summary>
    public int EstimateSize()
    {
        int size = 0;
        foreach (var tab in this)
        {
            size += tab.EstimateSize();
        }
        return size;
    }

    /// <summary>
    /// Writes all stash tabs to a new byte array.
    /// </summary>
    /// <param name="externalData">External game data for writing items. Uses built-in data if null.</param>
    /// <param name="targetVersion">Optional target version for format conversion.</param>
    /// <returns>The serialized stash save bytes.</returns>
    public byte[] ToBytes(IExternalData? externalData = null, uint? targetVersion = null)
    {
        byte[] buffer = new byte[EstimateSize()];
        var writer = new BitWriter(buffer);
        Write(ref writer, externalData ?? TxtFileExternalData.Default, targetVersion);
        return buffer.AsSpan(0, writer.BytesWritten).ToArray();
    }

    /// <summary>
    /// Writes all stash tabs to a BitWriter using the default external data.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="targetVersion">Optional target version for format conversion. If null, uses current Version.</param>
    public void Write(ref BitWriter writer, uint? targetVersion = null) => Write(ref writer, TxtFileExternalData.Default, targetVersion);

    /// <summary>
    /// Writes all stash tabs to a BitWriter.
    /// </summary>
    /// <param name="writer">The bit writer.</param>
    /// <param name="externalData">External game data for writing items.</param>
    /// <param name="targetVersion">Optional target version for format conversion. If null, uses current Version.</param>
    public void Write(ref BitWriter writer, IExternalData externalData, uint? targetVersion = null)
    {
        foreach (var tab in this)
        {
            tab.Write(ref writer, externalData, targetVersion);
        }
    }
}
