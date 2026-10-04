using System.Text;
using MBW.GHLinguist.Classification;

namespace MBW.GHLinguist;

/// <summary>Classifies source content with the .NET port of Linguist's classifier, without starting Ruby.</summary>
/// <remarks>
/// This is <see cref="LinguistRuntime.ClassifyDotNet" /> without a <see cref="LinguistRuntime" />: it reads the
/// language registry and classifier database from the deployed native asset directory and never loads the native
/// bridge or CRuby, so it costs no Ruby startup or memory. Rankings and scores are identical to
/// <see cref="LinguistRuntime.Classify" />. Instances are immutable and thread-safe; calls run in parallel.
/// </remarks>
/// <example>
/// <code>
/// LinguistClassifier classifier = LinguistClassifier.Load();
/// ClassificationResults results = classifier.Classify("class Example {}"u8);
/// </code>
/// </example>
public sealed class LinguistClassifier
{
    private const string LanguagesPath = "ghlinguist/languages.tsv";
    private const string ClassifierPath = "lib/linguist/samples.tsv";
    private static readonly Dictionary<string, LinguistClassifier> Loaded = new(StringComparer.Ordinal);

    private readonly LinguistContentClassifier _classifier;
    private readonly HashSet<ulong> _languageIds;

    private LinguistClassifier(IReadOnlyList<LinguistLanguage> languages, ClassifierDatabase database)
    {
        Languages = languages;
        _languageIds = [.. languages.Select(language => language.Id)];
        _classifier = LinguistContentClassifier.Create(database, languages);
    }

    /// <summary>Loads the classifier from deployment-local native Linguist assets for the current platform.</summary>
    /// <remarks>
    /// The assets come from the same <c>MBW.GHLinguist</c> directory <see cref="LinguistRuntime.Create" /> uses, and
    /// the files read are checked against its provenance. The data loads once per process; later calls return the
    /// same instance.
    /// </remarks>
    /// <returns>A loaded classifier.</returns>
    /// <exception cref="PlatformNotSupportedException">The process is not x64 Windows or Linux, or single-file deployment prevents locating the native asset directory.</exception>
    /// <exception cref="LinguistException">The deployed assets are missing, fail integrity validation, or cannot be read.</exception>
    public static LinguistClassifier Load() => Get(NativeLinguistRuntimeBackend.GetNativeAssetRoot());

    /// <summary>Returns the process-wide classifier for <paramref name="assetRoot" />, loading it on first use.</summary>
    internal static LinguistClassifier Get(string assetRoot)
    {
        assetRoot = Path.GetFullPath(assetRoot);
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(assetRoot, out LinguistClassifier? classifier))
            {
                NativeAssetIntegrity.ValidateFiles(assetRoot, NativeAssetIntegrity.GetCurrentRuntimeIdentifier(), LanguagesPath, ClassifierPath);
                classifier = Load(assetRoot);
                Loaded.Add(assetRoot, classifier);
            }

            return classifier;
        }
    }

    /// <summary>Gets Linguist's language registry, in registry order, as <see cref="LinguistRuntime.Languages" /> reports it.</summary>
    public IReadOnlyList<LinguistLanguage> Languages { get; }

    /// <summary>Classifies source content exactly like <see cref="LinguistRuntime.Classify" />.</summary>
    /// <param name="data">Source bytes. At most the configured leading 50 KiB are considered.</param>
    /// <param name="options">Optional classifier filters and byte limit; <see langword="null" /> uses Linguist defaults.</param>
    /// <returns>Matches ordered by descending similarity.</returns>
    /// <exception cref="ArgumentException">A candidate language ID is not in <see cref="Languages" />.</exception>
    /// <exception cref="LinguistException">The classifier produced a score outside 0 to 1 beyond rounding.</exception>
    /// <example><code>ClassificationResults results = classifier.Classify("class Example {}"u8);</code></example>
    public ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions? options = null)
    {
        options ??= new ClassificationOptions();
        if (options.CandidateLanguageIds is { } candidates)
        {
            if (candidates.Count == 0)
            {
                return new ClassificationResults();
            }

            foreach (ulong languageId in candidates)
            {
                if (!_languageIds.Contains(languageId))
                {
                    throw new ArgumentException(
                        $"Candidate language ID {languageId} does not exist in the loaded Linguist registry.",
                        nameof(ClassificationOptions.CandidateLanguageIds));
                }
            }
        }

        return ClassifyValidated(data, options);
    }

    /// <summary>Classifies with options the caller already validated against the registry.</summary>
    internal ClassificationResults ClassifyValidated(ReadOnlySpan<byte> data, ClassificationOptions options) =>
        _classifier.Classify(data, options);

    private static LinguistClassifier Load(string assetRoot)
    {
        try
        {
            IReadOnlyList<LinguistLanguage> languages = ReadLanguages(File.ReadAllBytes(Path.Combine(assetRoot, LanguagesPath)));
            ClassifierDatabase database = ClassifierDatabase.Load(Path.Combine(assetRoot, ClassifierPath));
            return new LinguistClassifier(languages, database);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            throw new LinguistException($"Unable to load the Linguist classifier data: {exception.Message}", exception);
        }
    }

    /// <summary>Reads <c>languages.tsv</c>, which eng/linguist/generate-samples.rb writes from <c>Linguist::Language.all</c>.</summary>
    /// <remarks>
    /// Each language is a line <c>language id group-id? type? popular wrap name fs_name? color? tm_scope ace_mode?
    /// codemirror_mode? codemirror_mime_type?</c> (<c>?</c> fields are empty for nil), followed by the lines
    /// <c>aliases</c>, <c>extensions</c>, <c>interpreters</c> and <c>filenames</c>, each listing its values.
    /// </remarks>
    internal static IReadOnlyList<LinguistLanguage> ReadLanguages(ReadOnlySpan<byte> text)
    {
        TabSeparatedReader reader = new(text, "language registry");
        List<LinguistLanguage> languages = [];
        while (reader.NextLine(out ReadOnlySpan<byte> kind))
        {
            if (!kind.SequenceEqual("language"u8))
            {
                throw reader.Error("expected a language line");
            }

            ulong id = reader.OptionalUInt64() ?? throw reader.Error("the language ID is empty");
            ulong? groupId = reader.OptionalUInt64();
            LanguageType type = reader.OptionalString() switch
            {
                null => LanguageType.Unknown,
                "data" => LanguageType.Data,
                "markup" => LanguageType.Markup,
                "programming" => LanguageType.Programming,
                "prose" => LanguageType.Prose,
                _ => throw reader.Error("unknown language type"),
            };
            bool popular = reader.Int32() != 0;
            bool wrap = reader.Int32() != 0;
            LinguistLanguage language = new()
            {
                Id = id,
                GroupLanguageId = groupId,
                Type = type,
                IsPopular = popular,
                WrapLines = wrap,
                Name = reader.String(),
                FileSystemName = reader.OptionalString(),
                Color = reader.OptionalString(),
                TextMateScope = reader.String(),
                AceMode = reader.OptionalString(),
                CodeMirrorMode = reader.OptionalString(),
                CodeMirrorMimeType = reader.OptionalString(),
                Aliases = List(ref reader, "aliases"u8),
                Extensions = List(ref reader, "extensions"u8),
                Interpreters = List(ref reader, "interpreters"u8),
                Filenames = List(ref reader, "filenames"u8),
            };
            languages.Add(language);
        }

        return languages.AsReadOnly();

        static string[] List(ref TabSeparatedReader reader, ReadOnlySpan<byte> expectedKind)
        {
            if (!reader.NextLine(out ReadOnlySpan<byte> kind) || !kind.SequenceEqual(expectedKind))
            {
                throw reader.Error($"expected a {Encoding.ASCII.GetString(expectedKind)} line");
            }

            return reader.RemainingStrings();
        }
    }
}
