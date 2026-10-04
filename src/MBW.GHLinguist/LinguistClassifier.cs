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
/// LinguistClassifier classifier = LinguistClassifier.Create();
/// ClassificationResults results = classifier.Classify("class Example {}"u8);
/// </code>
/// </example>
public sealed class LinguistClassifier
{
    private const string LanguagesPath = "ghlinguist/languages.bin";
    private const string ClassifierPath = "lib/linguist/samples.bin";
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
    public static LinguistClassifier Create() => Get(NativeLinguistRuntimeBackend.GetNativeAssetRoot());

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

    /// <summary>Reads <c>languages.bin</c>, which eng/linguist/generate-samples.rb writes from <c>Linguist::Language.all</c>.</summary>
    /// <remarks>
    /// After the header: u32 count, then per language: u64 id, u64 group id (<c>ulong.MaxValue</c> for none), u8 type,
    /// u8 flags (1 popular, 2 wrap), strings name, fs_name?, color?, tm_scope, ace_mode?, codemirror_mode?,
    /// codemirror_mime_type?, then string lists aliases, extensions, interpreters and filenames.
    /// </remarks>
    internal static IReadOnlyList<LinguistLanguage> ReadLanguages(ReadOnlySpan<byte> bytes)
    {
        BinaryCursor reader = new(bytes, "GHLL"u8, "language registry");
        LinguistLanguage[] languages = new LinguistLanguage[reader.ReadCount()];
        for (int index = 0; index < languages.Length; index++)
        {
            ulong id = reader.ReadUInt64();
            ulong groupId = reader.ReadUInt64();
            LanguageType type = (LanguageType)reader.ReadByte();
            byte flags = reader.ReadByte();
            languages[index] = new LinguistLanguage
            {
                Id = id,
                GroupLanguageId = groupId == ulong.MaxValue ? null : groupId,
                Type = type is >= LanguageType.Unknown and <= LanguageType.Prose ? type : throw new FormatException($"Language {id} has unknown type {type}."),
                IsPopular = (flags & 1) != 0,
                WrapLines = (flags & 2) != 0,
                Name = reader.ReadString(),
                FileSystemName = reader.ReadOptionalString(),
                Color = reader.ReadOptionalString(),
                TextMateScope = reader.ReadString(),
                AceMode = reader.ReadOptionalString(),
                CodeMirrorMode = reader.ReadOptionalString(),
                CodeMirrorMimeType = reader.ReadOptionalString(),
                Aliases = reader.ReadStrings(),
                Extensions = reader.ReadStrings(),
                Interpreters = reader.ReadStrings(),
                Filenames = reader.ReadStrings(),
            };
        }

        reader.ExpectEnd();
        return Array.AsReadOnly(languages);
    }
}
