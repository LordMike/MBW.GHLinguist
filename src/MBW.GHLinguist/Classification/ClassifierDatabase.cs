using System.Buffers.Text;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MBW.GHLinguist.Classification;

/// <summary>Linguist's trained classifier (<c>vocabulary</c>, <c>icf</c> and <c>centroids</c>) in flat arrays.</summary>
/// <remarks>
/// Loaded from <c>ghlinguist/classifier.json</c>, which the native build writes from the same <c>samples_data.rb</c>
/// data Linguist's Ruby classifier loads. Centroids are stored inverted: for each vocabulary index, the centroids
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

    internal static ClassifierDatabase Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Reads <c>{"vocabulary": {term: index}, "icf": [..], "centroids": {name: {"index": value}}}</c>.</summary>
    /// <remarks>Ruby writes these from Hashes, so no object repeats a key.</remarks>
    internal static ClassifierDatabase Parse(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);
        List<(byte[] Term, int Index)>? vocabulary = null;
        List<double>? icf = null;
        List<string> centroidNames = [];
        List<int> centroidEnds = [];
        List<int> entryTerms = [];
        List<double> entryValues = [];
        bool hasCentroids = false;

        Read(ref reader, JsonTokenType.StartObject);
        while (Read(ref reader) == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("vocabulary"u8))
            {
                vocabulary = [];
                Read(ref reader, JsonTokenType.StartObject);
                while (Read(ref reader) == JsonTokenType.PropertyName)
                {
                    byte[] term = reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan.ToArray();
                    Read(ref reader, JsonTokenType.Number);
                    vocabulary.Add((term, reader.GetInt32()));
                }
            }
            else if (reader.ValueTextEquals("icf"u8))
            {
                icf = [];
                Read(ref reader, JsonTokenType.StartArray);
                while (Read(ref reader) == JsonTokenType.Number)
                {
                    icf.Add(reader.GetDouble());
                }
            }
            else if (reader.ValueTextEquals("centroids"u8))
            {
                hasCentroids = true;
                Read(ref reader, JsonTokenType.StartObject);
                while (Read(ref reader) == JsonTokenType.PropertyName)
                {
                    centroidNames.Add(reader.GetString()!);
                    Read(ref reader, JsonTokenType.StartObject);
                    while (Read(ref reader) == JsonTokenType.PropertyName)
                    {
                        if (!Utf8Parser.TryParse(reader.ValueSpan, out int term, out int consumed) || consumed != reader.ValueSpan.Length)
                        {
                            throw new FormatException($"The centroid '{centroidNames[^1]}' has a key that is not a vocabulary index.");
                        }

                        Read(ref reader, JsonTokenType.Number);
                        entryTerms.Add(term);
                        entryValues.Add(reader.GetDouble());
                    }

                    centroidEnds.Add(entryTerms.Count);
                }
            }
            else
            {
                reader.Skip();
            }
        }

        if (vocabulary is null || icf is null || !hasCentroids)
        {
            throw new FormatException("The classifier database lacks vocabulary, icf or centroids.");
        }

        if (icf.Count != vocabulary.Count)
        {
            throw new FormatException($"The classifier database has {vocabulary.Count} vocabulary terms but {icf.Count} icf values.");
        }

        int termCount = icf.Count;
        int[] postingStarts = new int[termCount + 1];
        foreach (int term in entryTerms)
        {
            if ((uint)term >= (uint)termCount)
            {
                throw new FormatException($"A centroid references vocabulary index {term}, outside 0..{termCount - 1}.");
            }

            postingStarts[term + 1]++;
        }

        for (int term = 0; term < termCount; term++)
        {
            postingStarts[term + 1] += postingStarts[term];
        }

        // Invert to per-term postings; walking centroids in order keeps each term's postings in centroid order.
        int[] cursor = postingStarts[..termCount];
        int[] postingCentroids = new int[entryTerms.Count];
        double[] postingValues = new double[entryTerms.Count];
        for (int centroid = 0, entry = 0; centroid < centroidEnds.Count; centroid++)
        {
            for (; entry < centroidEnds[centroid]; entry++)
            {
                int position = cursor[entryTerms[entry]]++;
                postingCentroids[position] = centroid;
                postingValues[position] = entryValues[entry];
            }
        }

        return new ClassifierDatabase(
            new VocabularyTable(vocabulary),
            [.. icf],
            [.. centroidNames],
            postingStarts,
            postingCentroids,
            postingValues);
    }

    private static JsonTokenType Read(ref Utf8JsonReader reader, JsonTokenType? expected = null)
    {
        if (!reader.Read() || (expected is { } type && reader.TokenType != type))
        {
            throw new FormatException($"The classifier database is malformed at byte {reader.TokenStartIndex}.");
        }

        return reader.TokenType;
    }
}

/// <summary>Open-addressing map from token bytes to vocabulary index, probed without allocating.</summary>
internal sealed class VocabularyTable
{
    private readonly int[] _slots;
    private readonly int _mask;
    private readonly byte[] _keyBytes;
    private readonly int[] _keyStarts;

    /// <param name="vocabulary">Each term's UTF-8 bytes and index; the indexes must be exactly 0..count-1.</param>
    internal VocabularyTable(IReadOnlyCollection<(byte[] Term, int Index)> vocabulary)
    {
        int termCount = vocabulary.Count;
        int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, termCount * 4));
        _slots = new int[capacity];
        Array.Fill(_slots, -1);
        _mask = capacity - 1;
        _keyStarts = new int[termCount + 1];
        byte[][] keys = new byte[termCount][];
        foreach ((byte[] term, int index) in vocabulary)
        {
            if ((uint)index >= (uint)termCount || keys[index] is not null)
            {
                throw new FormatException($"The classifier vocabulary index {index} is invalid or repeated.");
            }

            keys[index] = term;
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
