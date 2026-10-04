using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace MBW.GHLinguist.Classification;

/// <summary>
/// Reads the little-endian files eng/linguist/generate-samples.rb writes: a 4-byte magic and u32 version 1, then
/// u32 counts, u64 IDs, f64 values, and strings stored as a u32 byte length (<c>0xffffffff</c> for nil) and UTF-8.
/// </summary>
internal ref struct BinaryCursor
{
    private const uint NullLength = uint.MaxValue;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _name;
    private int _position;

    internal BinaryCursor(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> magic, string name)
    {
        _bytes = bytes;
        _name = name;
        if (bytes.Length < 8 || !bytes.StartsWith(magic) || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 1)
        {
            throw Error("it is not a version 1 file");
        }

        _position = 8;
    }

    internal int ReadCount() => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(Take(4)));

    internal ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    internal byte ReadByte() => Take(1)[0];

    internal int[] ReadInt32s(int count) => ToArray<int>(Take(checked(count * 4)));

    internal double[] ReadDoubles(int count) => ToArray<double>(Take(checked(count * 8)));

    internal byte[] ReadBytes() => Take(ReadCount()).ToArray();

    internal string ReadString() => ReadOptionalString() ?? throw Error("a required string is nil");

    internal string? ReadOptionalString()
    {
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        return length == NullLength ? null : StrictUtf8.GetString(Take(checked((int)length)));
    }

    internal string[] ReadStrings()
    {
        string[] values = new string[ReadCount()];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = ReadString();
        }

        return values;
    }

    internal void SkipString() => Take(ReadCount());

    internal readonly void ExpectEnd()
    {
        if (_position != _bytes.Length)
        {
            throw Error("it has trailing bytes");
        }
    }

    private static T[] ToArray<T>(ReadOnlySpan<byte> bytes) where T : struct
    {
        // MBW.GHLinguist runs only on x64, so the file's little-endian values are already in machine order.
        return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || _position > _bytes.Length - length)
        {
            throw Error("it ends early");
        }

        ReadOnlySpan<byte> value = _bytes.Slice(_position, length);
        _position += length;
        return value;
    }

    private readonly FormatException Error(string reason) => new($"The {_name} is malformed: {reason} (at byte {_position}).");
}
