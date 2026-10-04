using System.Numerics;
using System.Globalization;
using System.Text;

namespace MBW.GHLinguist.Classification;

/// <summary>Linguist's trained classifier (<c>vocabulary</c>, <c>icf</c> and <c>centroids</c>) in flat arrays.</summary>
/// <remarks>
/// Loaded from the <c>samples_data.rb</c> that the native bundle ships and Linguist itself loads, so both
/// implementations read the same literals. Centroids are stored inverted: for each vocabulary index, the centroids
/// that contain it, in centroid order.
/// </remarks>
internal sealed class ClassifierDatabase
{
    private ClassifierDatabase(
        VocabularyTable vocabulary,
        double[] inverseClassFrequencies,
        string[] centroidNames,
        int[] postingStarts,
        int[] postingCentroids,
        double[] postingValues)
    {
        Vocabulary = vocabulary;
        InverseClassFrequencies = inverseClassFrequencies;
        CentroidNames = centroidNames;
        PostingStarts = postingStarts;
        PostingCentroids = postingCentroids;
        PostingValues = postingValues;
    }

    internal VocabularyTable Vocabulary { get; }

    internal double[] InverseClassFrequencies { get; }

    /// <summary>Centroid keys (a language's <c>fs_name</c> or <c>name</c>) in database order.</summary>
    internal string[] CentroidNames { get; }

    /// <summary>For vocabulary index <c>i</c>, postings live in <c>[PostingStarts[i], PostingStarts[i + 1])</c>.</summary>
    internal int[] PostingStarts { get; }

    internal int[] PostingCentroids { get; }

    internal double[] PostingValues { get; }

    internal static ClassifierDatabase Load(string samplesDataPath) => Parse(File.ReadAllBytes(samplesDataPath));

    internal static ClassifierDatabase Parse(ReadOnlySpan<byte> source)
    {
        RubyLiteralReader reader = new(source);
        reader.ExpectStatementPrefix();

        Dictionary<string, int>? vocabulary = null;
        List<double>? icf = null;
        List<(string Name, List<(int Index, double Value)> Values)>? centroids = null;

        reader.Expect((byte)'{');
        while (!reader.TryConsume((byte)'}'))
        {
            string key = reader.ReadString();
            reader.ExpectArrow();
            switch (key)
            {
                case "vocabulary":
                    vocabulary = ReadVocabulary(ref reader);
                    break;
                case "icf":
                    icf = ReadFloatArray(ref reader);
                    break;
                case "centroids":
                    centroids = ReadCentroids(ref reader);
                    break;
                default:
                    reader.SkipValue();
                    break;
            }

            reader.TryConsume((byte)',');
        }

        if (vocabulary is null || icf is null || centroids is null)
        {
            throw new FormatException("The classifier database lacks vocabulary, icf or centroids.");
        }

        if (icf.Count != vocabulary.Count)
        {
            throw new FormatException($"The classifier database has {vocabulary.Count} vocabulary terms but {icf.Count} icf values.");
        }

        int termCount = icf.Count;
        int[] postingCounts = new int[termCount + 1];
        foreach ((string _, List<(int Index, double Value)> values) in centroids)
        {
            foreach ((int index, double _) in values)
            {
                if ((uint)index >= (uint)termCount)
                {
                    throw new FormatException($"A centroid references vocabulary index {index}, outside 0..{termCount - 1}.");
                }

                postingCounts[index + 1]++;
            }
        }

        for (int index = 0; index < termCount; index++)
        {
            postingCounts[index + 1] += postingCounts[index];
        }

        int[] postingStarts = postingCounts;
        int[] cursor = postingStarts[..termCount];
        int[] postingCentroids = new int[postingStarts[termCount]];
        double[] postingValues = new double[postingStarts[termCount]];
        string[] centroidNames = new string[centroids.Count];
        for (int centroid = 0; centroid < centroids.Count; centroid++)
        {
            centroidNames[centroid] = centroids[centroid].Name;
            foreach ((int index, double value) in centroids[centroid].Values)
            {
                int position = cursor[index]++;
                postingCentroids[position] = centroid;
                postingValues[position] = value;
            }
        }

        return new ClassifierDatabase(
            new VocabularyTable(vocabulary, termCount),
            [.. icf],
            centroidNames,
            postingStarts,
            postingCentroids,
            postingValues);
    }

    private static Dictionary<string, int> ReadVocabulary(ref RubyLiteralReader reader)
    {
        Dictionary<string, int> vocabulary = new(StringComparer.Ordinal);
        reader.Expect((byte)'{');
        while (!reader.TryConsume((byte)'}'))
        {
            string term = reader.ReadString();
            reader.ExpectArrow();
            int index = reader.ReadInteger();
            if (!vocabulary.TryAdd(term, index))
            {
                throw new FormatException($"The classifier vocabulary repeats the term '{term}'.");
            }

            reader.TryConsume((byte)',');
        }

        return vocabulary;
    }

    private static List<double> ReadFloatArray(ref RubyLiteralReader reader)
    {
        List<double> values = [];
        reader.Expect((byte)'[');
        while (!reader.TryConsume((byte)']'))
        {
            values.Add(reader.ReadFloat());
            reader.TryConsume((byte)',');
        }

        return values;
    }

    private static List<(string, List<(int, double)>)> ReadCentroids(ref RubyLiteralReader reader)
    {
        List<(string, List<(int, double)>)> centroids = [];
        reader.Expect((byte)'{');
        while (!reader.TryConsume((byte)'}'))
        {
            string name = reader.ReadString();
            reader.ExpectArrow();
            List<(int, double)> values = [];
            HashSet<int> seen = [];
            reader.Expect((byte)'{');
            while (!reader.TryConsume((byte)'}'))
            {
                int index = reader.ReadInteger();
                reader.ExpectArrow();
                if (!seen.Add(index))
                {
                    throw new FormatException($"The centroid '{name}' repeats vocabulary index {index}.");
                }

                values.Add((index, reader.ReadFloat()));
                reader.TryConsume((byte)',');
            }

            centroids.Add((name, values));
            reader.TryConsume((byte)',');
        }

        return centroids;
    }

    /// <summary>Reads the subset of Ruby literal syntax that <c>PP.pp</c> emits for the samples database.</summary>
    private ref struct RubyLiteralReader(ReadOnlySpan<byte> source)
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private readonly ReadOnlySpan<byte> _source = source;
        private int _position;

        internal void ExpectStatementPrefix()
        {
            // "# frozen_string_literal: true\nDATA = {...}"
            SkipWhitespaceAndComments();
            if (!_source[_position..].StartsWith("DATA"u8))
            {
                throw Error("expected 'DATA ='");
            }

            _position += 4;
            Expect((byte)'=');
        }

        internal void Expect(byte value)
        {
            if (!TryConsume(value))
            {
                throw Error($"expected '{(char)value}'");
            }
        }

        internal void ExpectArrow()
        {
            Expect((byte)'=');
            if (_position >= _source.Length || _source[_position] != (byte)'>')
            {
                throw Error("expected '=>'");
            }

            _position++;
        }

        internal bool TryConsume(byte value)
        {
            SkipWhitespaceAndComments();
            if (_position < _source.Length && _source[_position] == value)
            {
                _position++;
                return true;
            }

            return false;
        }

        internal void SkipValue()
        {
            SkipWhitespaceAndComments();
            if (_position >= _source.Length)
            {
                throw Error("unexpected end of input");
            }

            switch (_source[_position])
            {
                case (byte)'"':
                    ReadString();
                    return;
                case (byte)'{':
                    _position++;
                    while (!TryConsume((byte)'}'))
                    {
                        SkipValue();
                        ExpectArrow();
                        SkipValue();
                        TryConsume((byte)',');
                    }

                    return;
                case (byte)'[':
                    _position++;
                    while (!TryConsume((byte)']'))
                    {
                        SkipValue();
                        TryConsume((byte)',');
                    }

                    return;
                default:
                    ReadBareWord();
                    return;
            }
        }

        internal int ReadInteger()
        {
            ReadOnlySpan<byte> word = ReadBareWord();
            if (!int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            {
                throw Error($"expected an integer, found '{Encoding.UTF8.GetString(word)}'");
            }

            return value;
        }

        internal double ReadFloat()
        {
            // Ruby prints floats with the shortest round-trip digits; .NET parses them with correct rounding, so
            // the doubles are bit-identical to the ones Ruby holds.
            ReadOnlySpan<byte> word = ReadBareWord();
            if (word.IndexOfAny((byte)'.', (byte)'e') < 0 ||
                !double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value))
            {
                throw Error($"expected a float, found '{Encoding.UTF8.GetString(word)}'");
            }

            return value;
        }

        internal string ReadString()
        {
            Expect((byte)'"');
            StringBuilder builder = new();
            while (true)
            {
                if (_position >= _source.Length)
                {
                    throw Error("unterminated string");
                }

                byte value = _source[_position++];
                if (value == (byte)'"')
                {
                    return builder.ToString();
                }

                if (value >= 0x80)
                {
                    // String#inspect keeps printable UTF-8 as-is; the vocabulary has none today.
                    int start = _position - 1;
                    while (_position < _source.Length && _source[_position] >= 0x80)
                    {
                        _position++;
                    }

                    builder.Append(StrictUtf8.GetString(_source[start.._position]));
                    continue;
                }

                if (value != (byte)'\\')
                {
                    builder.Append((char)value);
                    continue;
                }

                if (_position >= _source.Length)
                {
                    throw Error("unterminated escape");
                }

                byte escape = _source[_position++];
                switch (escape)
                {
                    case (byte)'n': builder.Append('\n'); break;
                    case (byte)'t': builder.Append('\t'); break;
                    case (byte)'r': builder.Append('\r'); break;
                    case (byte)'f': builder.Append('\f'); break;
                    case (byte)'v': builder.Append('\v'); break;
                    case (byte)'a': builder.Append('\a'); break;
                    case (byte)'b': builder.Append('\b'); break;
                    case (byte)'e': builder.Append('\u001b'); break;
                    case (byte)'"' or (byte)'\\' or (byte)'#': builder.Append((char)escape); break;
                    case (byte)'x':
                        builder.Append((char)ReadHex(2, 2));
                        break;
                    case (byte)'u':
                        if (TryConsumeRaw((byte)'{'))
                        {
                            builder.Append(char.ConvertFromUtf32(ReadHex(1, 6)));
                            if (!TryConsumeRaw((byte)'}'))
                            {
                                throw Error("expected '}' after \\u{");
                            }
                        }
                        else
                        {
                            builder.Append((char)ReadHex(4, 4));
                        }

                        break;
                    default:
                        throw Error($"unsupported string escape '\\{(char)escape}'");
                }
            }
        }

        private int ReadHex(int minimumDigits, int maximumDigits)
        {
            int value = 0;
            int digits = 0;
            while (digits < maximumDigits && _position < _source.Length && char.IsAsciiHexDigit((char)_source[_position]))
            {
                value = (value * 16) + HexValue(_source[_position]);
                _position++;
                digits++;
            }

            if (digits < minimumDigits || (value > 0x7f && maximumDigits == 2))
            {
                // A \xHH escape above 0x7f means a non-UTF-8 byte string, which the vocabulary cannot contain.
                throw Error("unsupported hexadecimal escape");
            }

            return value;
        }

        private static int HexValue(byte digit) => digit <= (byte)'9' ? digit - '0' : (digit | 0x20) - 'a' + 10;

        private bool TryConsumeRaw(byte value)
        {
            if (_position < _source.Length && _source[_position] == value)
            {
                _position++;
                return true;
            }

            return false;
        }

        private ReadOnlySpan<byte> ReadBareWord()
        {
            SkipWhitespaceAndComments();
            int start = _position;
            while (_position < _source.Length && (char.IsAsciiLetterOrDigit((char)_source[_position]) ||
                _source[_position] is (byte)'-' or (byte)'+' or (byte)'.' or (byte)'_'))
            {
                _position++;
            }

            if (_position == start)
            {
                throw Error("expected a value");
            }

            return _source[start.._position];
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _source.Length)
            {
                byte value = _source[_position];
                if (value is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
                {
                    _position++;
                }
                else if (value == (byte)'#')
                {
                    int end = _source[_position..].IndexOf((byte)'\n');
                    _position = end < 0 ? _source.Length : _position + end + 1;
                }
                else
                {
                    return;
                }
            }
        }

        private readonly FormatException Error(string message) =>
            new($"Unable to read the classifier database at byte {_position}: {message}.");
    }
}

/// <summary>Open-addressing map from token bytes to vocabulary index, probed without allocating.</summary>
internal sealed class VocabularyTable
{
    private readonly int[] _slots;
    private readonly int _mask;
    private readonly byte[] _keyBytes;
    private readonly int[] _keyStarts;

    internal VocabularyTable(Dictionary<string, int> vocabulary, int termCount)
    {
        int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, termCount * 4));
        _slots = new int[capacity];
        Array.Fill(_slots, -1);
        _mask = capacity - 1;
        _keyStarts = new int[termCount + 1];
        byte[][] keys = new byte[termCount][];
        foreach ((string term, int index) in vocabulary)
        {
            if ((uint)index >= (uint)termCount || keys[index] is not null)
            {
                throw new FormatException($"The classifier vocabulary index {index} is invalid or repeated.");
            }

            keys[index] = Encoding.UTF8.GetBytes(term);
        }

        using MemoryStream bytes = new();
        for (int index = 0; index < termCount; index++)
        {
            _keyStarts[index] = (int)bytes.Length;
            bytes.Write(keys[index]);
            int slot = Hash(keys[index]) & _mask;
            while (_slots[slot] >= 0)
            {
                slot = (slot + 1) & _mask;
            }

            _slots[slot] = index;
        }

        _keyStarts[termCount] = (int)bytes.Length;
        _keyBytes = bytes.ToArray();
    }

    /// <summary>Returns the vocabulary index of <paramref name="token" />, or -1.</summary>
    /// <remarks>
    /// Tokenizer output is a binary Ruby string. Ruby's <c>Hash#key?</c> only matches such a string against the
    /// UTF-8 vocabulary when it is ASCII-only, so tokens with high bytes are never in the vocabulary.
    /// </remarks>
    internal int Find(ReadOnlySpan<byte> token)
    {
        if (System.Text.Ascii.IsValid(token) is false)
        {
            return -1;
        }

        int slot = Hash(token) & _mask;
        while (true)
        {
            int index = _slots[slot];
            if (index < 0)
            {
                return -1;
            }

            int start = _keyStarts[index];
            if (_keyBytes.AsSpan(start, _keyStarts[index + 1] - start).SequenceEqual(token))
            {
                return index;
            }

            slot = (slot + 1) & _mask;
        }
    }

    private static int Hash(ReadOnlySpan<byte> key)
    {
        uint hash = 2166136261;
        foreach (byte value in key)
        {
            hash = (hash ^ value) * 16777619;
        }

        return (int)(hash ^ (hash >> 15));
    }
}
