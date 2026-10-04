using System.Numerics;
using System.Text;

namespace MBW.GHLinguist.Classification;

/// <summary>Linguist's trained classifier (<c>vocabulary</c>, <c>icf</c> and <c>centroids</c>) in flat arrays.</summary>
/// <remarks>
/// Loaded from <c>lib/linguist/samples.bin</c>, the same file Linguist's Ruby classifier loads in the native bundle. Centroids are stored inverted: for each vocabulary index, the centroids
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

    /// <summary>Reads <c>samples.bin</c>; its layout is described in <c>ruby/linguist/samples_data.rb</c>.</summary>
    internal static ClassifierDatabase Parse(ReadOnlySpan<byte> bytes)
    {
        BinaryCursor reader = new(bytes, "GHLS"u8, "classifier database");
        for (int section = 0; section < 3; section++)
        {
            // extnames, interpreters and filenames, which only Linguist's language registry uses.
            for (int entry = reader.ReadCount(); entry > 0; entry--)
            {
                reader.SkipString();
                for (int value = reader.ReadCount(); value > 0; value--)
                {
                    reader.SkipString();
                }
            }
        }

        int termCount = reader.ReadCount();
        (byte[] Term, int Index)[] vocabulary = new (byte[], int)[termCount];
        for (int term = 0; term < termCount; term++)
        {
            vocabulary[term] = (reader.ReadBytes(), reader.ReadCount());
        }

        if (reader.ReadCount() != termCount)
        {
            throw new FormatException("The classifier database has a different number of icf values than vocabulary terms.");
        }

        double[] icf = reader.ReadDoubles(termCount);
        string[] centroidNames = new string[reader.ReadCount()];
        int[][] centroidTerms = new int[centroidNames.Length][];
        double[][] centroidValues = new double[centroidNames.Length][];
        int[] postingStarts = new int[termCount + 1];
        for (int centroid = 0; centroid < centroidNames.Length; centroid++)
        {
            centroidNames[centroid] = reader.ReadString();
            int count = reader.ReadCount();
            centroidTerms[centroid] = reader.ReadInt32s(count);
            centroidValues[centroid] = reader.ReadDoubles(count);
            foreach (int term in centroidTerms[centroid])
            {
                if ((uint)term >= (uint)termCount)
                {
                    throw new FormatException($"A centroid references vocabulary index {term}, outside 0..{termCount - 1}.");
                }

                postingStarts[term + 1]++;
            }
        }

        reader.SkipString();
        reader.ExpectEnd();
        for (int term = 0; term < termCount; term++)
        {
            postingStarts[term + 1] += postingStarts[term];
        }

        // Invert to per-term postings; walking centroids in order keeps each term's postings in centroid order.
        int[] cursor = postingStarts[..termCount];
        int[] postingCentroids = new int[postingStarts[termCount]];
        double[] postingValues = new double[postingStarts[termCount]];
        for (int centroid = 0; centroid < centroidNames.Length; centroid++)
        {
            for (int entry = 0; entry < centroidTerms[centroid].Length; entry++)
            {
                int position = cursor[centroidTerms[centroid][entry]]++;
                postingCentroids[position] = centroid;
                postingValues[position] = centroidValues[centroid][entry];
            }
        }

        return new ClassifierDatabase(
            new VocabularyTable(vocabulary),
            icf,
            centroidNames,
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
