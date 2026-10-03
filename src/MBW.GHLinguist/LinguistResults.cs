namespace MBW.GHLinguist;

/// <summary>Identifies the managed wrapper, native ABI, embedded Ruby, and Linguist data loaded by a runtime.</summary>
/// <example>
/// A runtime may report values such as ABI <c>1.0</c>, Linguist <c>9.6.0</c>, and Ruby <c>4.0.6</c>.
/// </example>
public sealed class LinguistVersionInfo
{
    /// <summary>Gets the native ABI major version.</summary>
    public uint AbiMajor { get; init; }

    /// <summary>Gets the native ABI minor version.</summary>
    public uint AbiMinor { get; init; }

    /// <summary>Gets the informational version of the managed <c>MBW.GHLinguist</c> package.</summary>
    public string PackageVersion { get; init; } = string.Empty;

    /// <summary>Gets the Git revision the native bridge was built from, or <see langword="null" /> when unavailable.</summary>
    /// <remarks>
    /// This is a source revision, not the package version; see <see cref="PackageVersion" /> for that. It is
    /// <see langword="null" /> when native build provenance does not supply it. A dirty build does not identify an
    /// exact source revision.
    /// </remarks>
    public string? NativeBridgeRevision { get; init; }

    /// <summary>Gets the embedded CRuby version.</summary>
    public string RubyVersion { get; init; } = string.Empty;

    /// <summary>Gets the GitHub Linguist version.</summary>
    public string LinguistVersion { get; init; } = string.Empty;

    /// <summary>Gets the pinned GitHub Linguist Git revision.</summary>
    public string LinguistRevision { get; init; } = string.Empty;

    /// <summary>Gets the SHA-256 digest of the classifier data loaded by the runtime.</summary>
    public string ClassifierSha256 { get; init; } = string.Empty;
}

/// <summary>Describes one language in GitHub Linguist's language registry.</summary>
/// <remarks>
/// Language identity, equality, and hashing use the stable numeric <see cref="Id" />. Names and aliases are metadata
/// and can change between Linguist revisions.
/// </remarks>
/// <example>
/// C# is represented as a programming language with aliases such as <c>csharp</c> and extension <c>.cs</c>.
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb" />
public sealed class LinguistLanguage : IEquatable<LinguistLanguage>
{
    private readonly IReadOnlyList<string> _aliases = [];
    private readonly IReadOnlyList<string> _extensions = [];
    private readonly IReadOnlyList<string> _interpreters = [];
    private readonly IReadOnlyList<string> _filenames = [];

    /// <summary>Gets Linguist's stable numeric language ID.</summary>
    public required ulong Id { get; init; }

    /// <summary>Gets the ID of this language's parent group, or <see langword="null" /> when it has no group.</summary>
    public ulong? GroupLanguageId { get; init; }

    /// <summary>Gets the canonical display name, for example <c>C#</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the filesystem-safe name, or <see langword="null" /> when the canonical name is used.</summary>
    public string? FileSystemName { get; init; }

    /// <summary>Gets the language category.</summary>
    public LanguageType Type { get; init; }

    /// <summary>Gets whether Linguist marks the language as popular.</summary>
    public bool IsPopular { get; init; }

    /// <summary>Gets whether rendered source should wrap long lines by default.</summary>
    public bool WrapLines { get; init; }

    /// <summary>Gets the suggested hexadecimal display color, or <see langword="null" /> when unspecified.</summary>
    public string? Color { get; init; }

    /// <summary>Gets the TextMate scope, for example <c>source.cs</c>.</summary>
    public string TextMateScope { get; init; } = string.Empty;

    /// <summary>Gets the Ace editor mode, or <see langword="null" /> when unspecified.</summary>
    public string? AceMode { get; init; }

    /// <summary>Gets the CodeMirror mode, or <see langword="null" /> when unspecified.</summary>
    public string? CodeMirrorMode { get; init; }

    /// <summary>Gets the CodeMirror MIME type, or <see langword="null" /> when unspecified.</summary>
    public string? CodeMirrorMimeType { get; init; }

    /// <summary>Gets the aliases accepted by Linguist for this language.</summary>
    public IReadOnlyList<string> Aliases
    {
        get => _aliases;
        init => _aliases = Copy(value);
    }

    /// <summary>Gets the filename extensions registered for this language.</summary>
    public IReadOnlyList<string> Extensions
    {
        get => _extensions;
        init => _extensions = Copy(value);
    }

    /// <summary>Gets the shebang interpreter names registered for this language.</summary>
    public IReadOnlyList<string> Interpreters
    {
        get => _interpreters;
        init => _interpreters = Copy(value);
    }

    /// <summary>Gets the exact special filenames registered for this language.</summary>
    public IReadOnlyList<string> Filenames
    {
        get => _filenames;
        init => _filenames = Copy(value);
    }

    /// <summary>Returns the canonical language name.</summary>
    /// <returns>The same value as <see cref="Name" />, for example <c>C#</c>.</returns>
    /// <example><code>Console.WriteLine(language); // C#</code></example>
    public override string ToString() => Name;

    /// <summary>Determines whether another language has the same stable Linguist language ID.</summary>
    /// <param name="other">The language to compare with this instance.</param>
    /// <returns><see langword="true" /> when both languages have the same <see cref="Id" />; otherwise <see langword="false" />.</returns>
    /// <example><code>bool sameLanguage = first.Equals(second);</code></example>
    public bool Equals(LinguistLanguage? other) => other is not null && Id == other.Id;

    /// <summary>Determines whether an object is a language with the same stable Linguist language ID.</summary>
    /// <param name="obj">The object to compare with this instance.</param>
    /// <returns><see langword="true" /> when <paramref name="obj" /> is a language with the same <see cref="Id" />.</returns>
    /// <example><code>bool sameLanguage = language.Equals((object)otherLanguage);</code></example>
    public override bool Equals(object? obj) => Equals(obj as LinguistLanguage);

    /// <summary>Returns a hash code derived from the stable Linguist language ID.</summary>
    /// <returns>The hash code of <see cref="Id" />.</returns>
    /// <example><code>var languages = new HashSet&lt;LinguistLanguage&gt; { language };</code></example>
    public override int GetHashCode() => Id.GetHashCode();

    private static IReadOnlyList<string> Copy(IEnumerable<string> value) =>
        Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
}

/// <summary>Describes the candidate languages produced by one detection strategy.</summary>
/// <example>An extension trace for <c>example.h</c> may contain C, C++, and Objective-C candidates.</example>
public sealed class StrategyTraceEntry
{
    private readonly IReadOnlyList<LinguistLanguage> _candidates = [];

    /// <summary>Gets the strategy that produced the candidate set.</summary>
    public DetectionStrategy Strategy { get; init; }

    /// <summary>Gets a copied read-only list of candidate languages.</summary>
    public IReadOnlyList<LinguistLanguage> Candidates
    {
        get => _candidates;
        init => _candidates = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }
}

/// <summary>Pairs a classified language with its similarity score.</summary>
/// <remarks>The score is a similarity value, not a probability or confidence percentage.</remarks>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/classifier.rb#L116-L149" />
public sealed class ClassificationResult
{
    /// <summary>Gets the classified language.</summary>
    public required LinguistLanguage Language { get; init; }

    /// <summary>Gets the classifier similarity score.</summary>
    public double Score { get; init; }
}

/// <summary>Contains the ordered results of direct content classification.</summary>
/// <remarks>
/// <see cref="Results" /> can be empty for unclassifiable input or an explicit empty candidate set. Scores are
/// similarities rather than probabilities; applications should establish their own admission policy instead of
/// interpreting a score as confidence. Equal scores retain the order supplied by Linguist.
/// </remarks>
/// <example>The first result may be C# with a score such as <c>0.93</c>.</example>
public sealed class ClassificationResults
{
    private readonly IReadOnlyList<ClassificationResult> _results = [];

    /// <summary>Gets the number of leading input bytes considered by the classifier.</summary>
    public int ConsideredBytes { get; init; }

    /// <summary>Gets classifier matches ordered from highest to lowest similarity score.</summary>
    public IReadOnlyList<ClassificationResult> Results
    {
        get => _results;
        init => _results = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }
}

/// <summary>Contains complete Linguist analysis for one blob.</summary>
/// <remarks>
/// <see cref="Language" /> is <see langword="null" /> exactly when <see cref="Strategy" /> is
/// <see cref="DetectionStrategy.None" />. <see cref="StrategyTrace" /> is empty unless requested, and line counts
/// are <see langword="null" /> unless requested.
/// </remarks>
/// <example>
/// A generated C# file may report <see cref="Language" /> as C#, <see cref="Strategy" /> as
/// <see cref="DetectionStrategy.Extension" />, and <see cref="IsGenerated" /> as <see langword="true" />.
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/blob_helper.rb" />
public sealed class BlobAnalysis
{
    private readonly IReadOnlyList<StrategyTraceEntry> _strategyTrace = [];

    /// <summary>Gets the detected language, or <see langword="null" /> when Linguist found no language.</summary>
    public LinguistLanguage? Language { get; init; }

    /// <summary>Gets the strategy that selected the detected language.</summary>
    public DetectionStrategy Strategy { get; init; }

    /// <summary>Gets whether the supplied blob contained no bytes.</summary>
    public bool IsEmpty { get; init; }

    /// <summary>Gets whether Linguist's inexpensive initial checks consider the blob likely binary.</summary>
    public bool IsLikelyBinary { get; init; }

    /// <summary>Gets whether Linguist classifies the blob as binary.</summary>
    public bool IsBinary { get; init; }

    /// <summary>Gets whether Linguist classifies the blob as text.</summary>
    public bool IsText { get; init; }

    /// <summary>Gets whether the blob is a recognized image format.</summary>
    public bool IsImage { get; init; }

    /// <summary>Gets whether the blob is a recognized solid-model format.</summary>
    public bool IsSolidModel { get; init; }

    /// <summary>Gets whether the blob is recognized as comma-separated values.</summary>
    public bool IsCsv { get; init; }

    /// <summary>Gets whether the blob is a PDF document.</summary>
    public bool IsPdf { get; init; }

    /// <summary>Gets whether Linguist considers the blob too large for normal rendering.</summary>
    public bool IsLarge { get; init; }

    /// <summary>Gets whether GitHub-style rendering may display the blob.</summary>
    public bool IsViewable { get; init; }

    /// <summary>Gets whether syntax colorization is safe for the blob.</summary>
    public bool IsSafeToColorize { get; init; }

    /// <summary>Gets whether an unusually high proportion of lines are very long.</summary>
    public bool HasHighRatioOfLongLines { get; init; }

    /// <summary>Gets whether the content is a Git LFS pointer.</summary>
    public bool IsLfsPointer { get; init; }

    /// <summary>Gets whether the input path matches Linguist's vendored-code rules.</summary>
    public bool IsVendored { get; init; }

    /// <summary>Gets whether the input path matches Linguist's documentation rules.</summary>
    public bool IsDocumentation { get; init; }

    /// <summary>Gets whether Linguist considers the file generated.</summary>
    public bool IsGenerated { get; init; }

    /// <summary>Gets whether the blob is eligible for normal language detection.</summary>
    public bool IsDetectable { get; init; }

    /// <summary>Gets whether this supplied blob is eligible for language statistics.</summary>
    /// <remarks>
    /// This is a per-blob Linguist decision. It does not parse <c>.gitattributes</c>, apply repository overrides, or
    /// aggregate repository language totals.
    /// </remarks>
    public bool IsIncludedInLanguageStatistics { get; init; }

    /// <summary>Gets the detected MIME type, for example <c>text/plain</c>.</summary>
    public string MimeType { get; init; } = string.Empty;

    /// <summary>Gets the complete content type, potentially including a character set.</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Gets the suggested content disposition, for example <c>inline</c> or <c>attachment</c>.</summary>
    public string Disposition { get; init; } = string.Empty;

    /// <summary>Gets the detected encoding name, or <see langword="null" /> when unavailable.</summary>
    public string? Encoding { get; init; }

    /// <summary>Gets the corresponding Ruby encoding name, or <see langword="null" /> when unavailable.</summary>
    public string? RubyEncoding { get; init; }

    /// <summary>Gets the detected language's TextMate scope, or <see langword="null" /> when no language was detected.</summary>
    public string? TextMateScope { get; init; }

    /// <summary>Gets Linguist's physical line count when requested, or <see langword="null" /> otherwise.</summary>
    /// <remarks>
    /// This rendering-oriented count is zero for non-viewable blobs, including binary input and files larger
    /// than 1 MiB. Zero does not necessarily mean the supplied blob was empty; use <see cref="IsEmpty" />.
    /// </remarks>
    public ulong? LineCount { get; init; }

    /// <summary>Gets Linguist's nonblank line count when requested, or <see langword="null" /> otherwise.</summary>
    /// <remarks>
    /// Comment-only lines are counted. Like <see cref="LineCount" />, this is zero for non-viewable blobs,
    /// including binary input and files larger than 1 MiB; it is not a count of executable source lines.
    /// </remarks>
    public ulong? SourceLineCount { get; init; }

    /// <summary>Gets the ordered detection trace when requested, or an empty list otherwise.</summary>
    public IReadOnlyList<StrategyTraceEntry> StrategyTrace
    {
        get => _strategyTrace;
        init => _strategyTrace = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }

    /// <summary>Verifies that <see cref="Language" /> and <see cref="Strategy" /> are both set or both unset.</summary>
    internal BlobAnalysis EnsureConsistent()
    {
        if ((Language is null) != (Strategy == DetectionStrategy.None))
        {
            throw new LinguistException("A detected language and its selecting strategy must either both be present or both be absent.");
        }

        return this;
    }
}
