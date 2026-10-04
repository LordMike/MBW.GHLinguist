using System.Numerics;
using System.Text;

namespace MBW.GHLinguist.Classification;

/// <summary>Linguist's trained classifier (<c>vocabulary</c>, <c>icf</c> and <c>centroids</c>) in flat arrays.</summary>
/// <remarks>
/// Loaded from <c>lib/linguist/samples.tsv</c>, the same file Linguist's Ruby classifier loads in the native bundle. Centroids are stored inverted: for each vocabulary index, the centroids
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

    /// <summary>Reads <c>samples.tsv</c>; its layout is described in <c>ruby/linguist/samples_data.rb</c>.</summary>
    internal static ClassifierDatabase Parse(ReadOnlySpan<byte> text)
    {
        TabSeparatedReader reader = new(text, "classifier database");
        List<(byte[] Term, int Index)> vocabulary = [];
        List<double> icf = [];
        List<string> centroidNames = [];
        List<int> centroidEnds = [];
        List<int> entryTerms = [];
        List<double> entryValues = [];
        while (reader.NextLine(out ReadOnlySpan<byte> kind))
        {
            if (kind.SequenceEqual("vocabulary"u8))
            {
                vocabulary.Add((reader.Field().ToArray(), reader.Int32()));
                reader.ExpectEndOfLine();
            }
            else if (kind.SequenceEqual("icf"u8))
            {
                while (reader.TryField(out ReadOnlySpan<byte> value))
                {
                    icf.Add(reader.ParseDouble(value));
                }
            }
            else if (kind.SequenceEqual("centroid"u8))
            {
                centroidNames.Add(reader.String());
                while (reader.TryField(out ReadOnlySpan<byte> term))
                {
                    entryTerms.Add(reader.ParseInt32(term));
                    entryValues.Add(reader.Double());
                }

                centroidEnds.Add(entryTerms.Count);
            }
            else if (!kind.SequenceEqual("extnames"u8) && !kind.SequenceEqual("interpreters"u8) &&
                !kind.SequenceEqual("filenames"u8) && !kind.SequenceEqual("sha256"u8))
            {
                // extnames, interpreters, filenames and sha256 are for Linguist's Ruby side only.
                throw reader.Error("unknown line kind");
            }
        }

        int termCount = vocabulary.Count;
        if (icf.Count != termCount)
        {
            throw new FormatException($"The classifier database has {termCount} vocabulary terms but {icf.Count} icf values.");
        }

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
