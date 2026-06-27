// SpanRead.cs
//
// Small internal big-endian span helpers shared by the U8 and FST parsers (both use 24-bit name offsets
// and null-terminated names in a string pool). u32/u16/u64 reads use BinaryPrimitives directly.

using System.Text;

namespace Module.WiiUTools;

internal static class SpanRead
{
    /// <summary>Read a big-endian unsigned 24-bit integer at <paramref name="offset"/>.</summary>
    public static uint U24BigEndian(ReadOnlySpan<byte> data, int offset)
        => (uint)((data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2]);

    /// <summary>Read a null-terminated ASCII string at <paramref name="offset"/>; "" if out of range.</summary>
    public static string CString(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset >= data.Length) return "";
        var slice = data[offset..];
        var end = slice.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end >= 0 ? slice[..end] : slice);
    }
}
