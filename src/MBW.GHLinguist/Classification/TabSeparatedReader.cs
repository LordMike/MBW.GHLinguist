using System.Globalization;
using System.Text;

namespace MBW.GHLinguist.Classification;

/// <summary>
/// Reads the tab-separated files eng/linguist/generate-samples.rb writes: one record per <c>\n</c>-terminated line,
/// fields separated by tabs, an empty field meaning nil, and floats in Ruby's shortest round-trip digits, which .NET
/// parses back to the same doubles.
/// </summary>
internal ref struct TabSeparatedReader(ReadOnlySpan<byte> text, string name)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private ReadOnlySpan<byte> _remaining = text;
    private ReadOnlySpan<byte> _line;
    private bool _hasField;
    private int _lineNumber;

    /// <summary>Moves to the next line and returns its first field, the line's kind.</summary>
    internal bool NextLine(out ReadOnlySpan<byte> kind)
    {
        if (_remaining.IsEmpty)
        {
            kind = default;
            return false;
        }

        int end = _remaining.IndexOf((byte)'\n');
        if (end < 0)
        {
            throw Error("the last line is not terminated");
        }

        _line = _remaining[..end];
        _hasField = true;
        _remaining = _remaining[(end + 1)..];
        _lineNumber++;
        kind = Field();
        return true;
    }

    internal bool TryField(out ReadOnlySpan<byte> field)
    {
        if (!_hasField)
        {
            field = default;
            return false;
        }

        field = Field();
        return true;
    }

    internal ReadOnlySpan<byte> Field()
    {
        if (!_hasField)
        {
            throw Error("a field is missing");
        }

        int tab = _line.IndexOf((byte)'\t');
        if (tab < 0)
        {
            _hasField = false;
            return _line;
        }

        ReadOnlySpan<byte> field = _line[..tab];
        _line = _line[(tab + 1)..];
        return field;
    }

    internal string String() => OptionalString() ?? throw Error("a required field is empty");

    internal string? OptionalString() => Field() is { IsEmpty: false } field ? StrictUtf8.GetString(field) : null;

    internal string[] RemainingStrings()
    {
        List<string> values = [];
        while (TryField(out ReadOnlySpan<byte> field))
        {
            values.Add(field.IsEmpty ? throw Error("a list value is empty") : StrictUtf8.GetString(field));
        }

        return [.. values];
    }

    internal int Int32() => ParseInt32(Field());

    internal ulong? OptionalUInt64() => Field() is { IsEmpty: false } field
        ? ulong.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) ? value : throw Error("expected an integer")
        : null;

    internal double Double() => ParseDouble(Field());

    internal int ParseInt32(ReadOnlySpan<byte> field) =>
        int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : throw Error("expected an integer");

    internal double ParseDouble(ReadOnlySpan<byte> field) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value
            : throw Error("expected a finite float");

    internal readonly FormatException Error(string reason) => new($"The {name} is malformed at line {_lineNumber}: {reason}.");

    internal readonly void ExpectEndOfLine()
    {
        if (_hasField)
        {
            throw Error("the line has extra fields");
        }
    }
}
