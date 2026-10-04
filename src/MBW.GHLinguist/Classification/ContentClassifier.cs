namespace MBW.GHLinguist.Classification;

/// <summary>Managed, thread-safe reimplementation of <c>Linguist::Classifier.classify</c>.</summary>
/// <remarks>
/// Every floating-point operation happens in the same order as Linguist's Ruby, so scores are bit-identical:
/// term frequencies accumulate in first-occurrence order, <c>1.0 + log(freq)</c> is multiplied by the term's ICF,
/// the L2 norm sums <c>x ** 2</c> (which Ruby computes as <c>x * x</c>) in that order, and each language's dot product adds terms in that order too.
/// Scoring walks an inverted index, touching only centroids that share a term with the input instead of every
/// language, and runs without the Ruby lock.
/// </remarks>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/classifier.rb#L91-L149" />
internal sealed class ContentClassifier
{
    private readonly ClassifierDatabase _database;

    [ThreadStatic]
    private static Scratch? t_scratch;

    internal ContentClassifier(ClassifierDatabase database)
    {
        _database = database;
    }

    internal int CentroidCount => _database.CentroidNames.Length;

    /// <summary>Scores <paramref name="data" /> against every centroid.</summary>
    /// <param name="data">The already truncated sample.</param>
    /// <param name="scores">Receives one score per centroid, in <see cref="ClassifierDatabase.CentroidNames" /> order; 0 for no shared terms.</param>
    /// <returns><see langword="false" /> when no token is in the vocabulary, where Linguist returns no results.</returns>
    internal bool Score(ReadOnlySpan<byte> data, Span<double> scores)
    {
        ClassifierDatabase database = _database;
        if (scores.Length != database.CentroidNames.Length)
        {
            throw new ArgumentException("The score buffer must have one entry per centroid.", nameof(scores));
        }

        Scratch scratch = t_scratch ??= new Scratch();
        scratch.EnsureCapacity(database.InverseClassFrequencies.Length);
        TermCounter counter = new(database.Vocabulary, scratch);
        try
        {
            LinguistTokenizer.Tokenize(data, ref counter);
            int termCount = counter.TermCount;
            scores.Clear();
            if (termCount == 0)
            {
                return false;
            }

            int[] terms = scratch.Terms;
            int[] counts = scratch.Counts;
            double[] weights = scratch.Weights;
            double[] icf = database.InverseClassFrequencies;

            // vec[idx] = (1.0 + Math.log(freq)) * icf[idx]
            for (int term = 0; term < termCount; term++)
            {
                int index = terms[term];
                double tf = 1.0 + Math.Log(counts[index]);
                weights[term] = tf * icf[index];
            }

            // l2_norm: vec.values.inject(0.0) { |sum, x| sum + x**2 }, then Math.sqrt. Ruby evaluates Float ** 2
            // as x * x (rb_float_pow), which differs from pow(x, 2) in the last bit for some inputs.
            double sum = 0.0;
            for (int term = 0; term < termCount; term++)
            {
                double weight = weights[term];
                sum += weight * weight;
            }

            double norm = Math.Sqrt(sum);
            int[] postingStarts = database.PostingStarts;
            int[] postingCentroids = database.PostingCentroids;
            double[] postingValues = database.PostingValues;
            for (int term = 0; term < termCount; term++)
            {
                // l2_normalize!, then similarity's sum += a[idx] * b[idx] for every centroid holding idx.
                double weight = weights[term] / norm;
                int index = terms[term];
                for (int posting = postingStarts[index]; posting < postingStarts[index + 1]; posting++)
                {
                    scores[postingCentroids[posting]] += weight * postingValues[posting];
                }
            }

            return true;
        }
        finally
        {
            counter.Reset();
        }
    }

    private sealed class Scratch
    {
        internal int[] Counts = [];
        internal int[] Terms = [];
        internal double[] Weights = [];

        internal void EnsureCapacity(int vocabularySize)
        {
            if (Counts.Length < vocabularySize)
            {
                Counts = new int[vocabularySize];
                Terms = new int[vocabularySize];
                Weights = new double[vocabularySize];
            }
        }
    }

    /// <summary>Counts vocabulary terms in first-occurrence order, like Ruby's insertion-ordered <c>Hash</c>.</summary>
    private struct TermCounter(VocabularyTable vocabulary, Scratch scratch) : ILinguistTokenSink
    {
        internal int TermCount;

        public void Add(scoped ReadOnlySpan<byte> token)
        {
            int index = vocabulary.Find(token);
            if (index < 0)
            {
                return;
            }

            if (scratch.Counts[index]++ == 0)
            {
                scratch.Terms[TermCount++] = index;
            }
        }

        internal readonly void Reset()
        {
            for (int term = 0; term < TermCount; term++)
            {
                scratch.Counts[scratch.Terms[term]] = 0;
            }
        }
    }
}
