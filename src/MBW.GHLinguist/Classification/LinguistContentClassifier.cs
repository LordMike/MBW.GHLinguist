namespace MBW.GHLinguist.Classification;

/// <summary>Managed equivalent of <c>GHLinguist::Bridge.classify</c>: language filtering, scoring and ranking.</summary>
/// <remarks>Immutable and safe to call concurrently from any number of threads.</remarks>
internal sealed class LinguistContentClassifier
{
    private readonly ContentClassifier _classifier;
    private readonly ClassificationLanguage[] _languages;
    private readonly Dictionary<ulong, ClassificationLanguage> _languagesById;

    [ThreadStatic]
    private static double[]? t_scores;

    private LinguistContentClassifier(ContentClassifier classifier, ClassificationLanguage[] languages)
    {
        _classifier = classifier;
        _languages = languages;
        _languagesById = languages.ToDictionary(language => language.Language.Id);
    }

    /// <param name="database">The classifier database the native runtime loads.</param>
    /// <param name="languages">The registry in <c>Linguist::Language.all</c> order.</param>
    internal static LinguistContentClassifier Create(ClassifierDatabase database, IReadOnlyList<LinguistLanguage> languages)
    {
        Dictionary<string, int> centroids = new(StringComparer.Ordinal);
        for (int index = 0; index < database.CentroidNames.Length; index++)
        {
            if (!centroids.TryAdd(database.CentroidNames[index], index))
            {
                throw new FormatException($"The classifier database repeats the centroid '{database.CentroidNames[index]}'.");
            }
        }

        ClassificationLanguage[] entries = new ClassificationLanguage[languages.Count];
        for (int index = 0; index < languages.Count; index++)
        {
            LinguistLanguage language = languages[index];

            // The bridge keeps languages whose centroid key, fs_name || name, exists in the database.
            int centroid = centroids.GetValueOrDefault(language.FileSystemName ?? language.Name, -1);
            entries[index] = new ClassificationLanguage(language, ToMask(language.Type), centroid);
        }

        return new LinguistContentClassifier(new ContentClassifier(database), entries);
    }

    /// <summary>Classifies <paramref name="data" />; <paramref name="options" /> must already be validated.</summary>
    internal ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions options)
    {
        int consideredBytes = Math.Min(data.Length, options.MaximumBytes);
        int centroidCount = _classifier.CentroidCount;
        double[] scores = t_scores is { } buffer && buffer.Length == centroidCount
            ? buffer
            : t_scores = new double[centroidCount];
        if (!_classifier.Score(data[..consideredBytes], scores))
        {
            return new ClassificationResults { ConsideredBytes = consideredBytes };
        }

        // Ruby inserts scores in the order of its language list (the registry, or the caller's candidates), then
        // sorts with sort_by { -score }. CRuby's sort uses the C library's qsort_r, which glibc implements as a
        // stable merge sort, so equal scores keep that list order. A Ruby built on another C library could order
        // exact ties differently; no two centroids share a weight, and no tie occurs in the test corpora.
        LanguageTypeMask allowedTypes = options.AllowedTypes;
        List<(double Score, int Order, LinguistLanguage Language)> ranked = [];
        if (options.CandidateLanguageIds is { } candidates)
        {
            for (int order = 0; order < candidates.Count; order++)
            {
                Add(_languagesById[candidates[order]], order);
            }
        }
        else
        {
            for (int order = 0; order < _languages.Length; order++)
            {
                Add(_languages[order], order);
            }
        }

        ranked.Sort(static (left, right) =>
            left.Score != right.Score ? right.Score.CompareTo(left.Score) : left.Order.CompareTo(right.Order));
        ClassificationResult[] results = new ClassificationResult[ranked.Count];
        for (int index = 0; index < results.Length; index++)
        {
            results[index] = new ClassificationResult { Language = ranked[index].Language, Score = ranked[index].Score };
        }

        return new ClassificationResults { ConsideredBytes = consideredBytes, Results = results };

        void Add(ClassificationLanguage language, int order)
        {
            if (language.Centroid < 0 || (allowedTypes & language.TypeMask) == 0)
            {
                return;
            }

            double score = scores[language.Centroid];
            if (score > 0.0)
            {
                ranked.Add((score, order, language.Language));
            }
        }
    }

    private static LanguageTypeMask ToMask(LanguageType type) => type switch
    {
        LanguageType.Data => LanguageTypeMask.Data,
        LanguageType.Markup => LanguageTypeMask.Markup,
        LanguageType.Programming => LanguageTypeMask.Programming,
        LanguageType.Prose => LanguageTypeMask.Prose,
        _ => 0,
    };

    private readonly record struct ClassificationLanguage(LinguistLanguage Language, LanguageTypeMask TypeMask, int Centroid);
}
