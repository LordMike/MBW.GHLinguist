namespace MBW.GHLinguist;

/// <summary>Identifies the managed wrapper, native ABI, embedded Ruby, and Linguist data loaded by a runtime.</summary>
/// <example>
/// A runtime may report values such as ABI <c>1.0</c>, Linguist <c>9.6.0</c>, and Ruby <c>4.0.1</c>.
/// </example>
public sealed class LinguistVersionInfo
{
    internal LinguistVersionInfo(
        uint abiMajor,
        uint abiMinor,
        string wrapperVersion,
        string rubyVersion,
        string linguistVersion,
        string linguistRevision,
        string classifierSha256)
    {
        AbiMajor = abiMajor;
        AbiMinor = abiMinor;
        WrapperVersion = wrapperVersion;
        RubyVersion = rubyVersion;
        LinguistVersion = linguistVersion;
        LinguistRevision = linguistRevision;
        ClassifierSha256 = classifierSha256;
    }

    /// <summary>Gets the native ABI major version.</summary>
    public uint AbiMajor { get; }

    /// <summary>Gets the native ABI minor version.</summary>
    public uint AbiMinor { get; }

    /// <summary>Gets the managed/native wrapper version.</summary>
    public string WrapperVersion { get; }

    /// <summary>Gets the embedded CRuby version.</summary>
    public string RubyVersion { get; }

    /// <summary>Gets the GitHub Linguist version.</summary>
    public string LinguistVersion { get; }

    /// <summary>Gets the pinned GitHub Linguist Git revision.</summary>
    public string LinguistRevision { get; }

    /// <summary>Gets the SHA-256 digest of the classifier data loaded by the runtime.</summary>
    public string ClassifierSha256 { get; }
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
    internal LinguistLanguage(
        ulong id,
        ulong? groupLanguageId,
        string name,
        string? fileSystemName,
        LanguageType type,
        bool isPopular,
        bool wrapLines,
        string? color,
        string textMateScope,
        string? aceMode,
        string? codeMirrorMode,
        string? codeMirrorMimeType,
        IEnumerable<string> aliases,
        IEnumerable<string> extensions,
        IEnumerable<string> interpreters,
        IEnumerable<string> filenames)
    {
        Id = id;
        GroupLanguageId = groupLanguageId;
        Name = name;
        FileSystemName = fileSystemName;
        Type = type;
        IsPopular = isPopular;
        WrapLines = wrapLines;
        Color = color;
        TextMateScope = textMateScope;
        AceMode = aceMode;
        CodeMirrorMode = codeMirrorMode;
        CodeMirrorMimeType = codeMirrorMimeType;
        Aliases = Array.AsReadOnly(aliases.ToArray());
        Extensions = Array.AsReadOnly(extensions.ToArray());
        Interpreters = Array.AsReadOnly(interpreters.ToArray());
        Filenames = Array.AsReadOnly(filenames.ToArray());
    }

    /// <summary>Gets Linguist's stable numeric language ID.</summary>
    public ulong Id { get; }

    /// <summary>Gets the ID of this language's parent group, or <see langword="null" /> when it has no group.</summary>
    public ulong? GroupLanguageId { get; }

    /// <summary>Gets the canonical display name, for example <c>C#</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the filesystem-safe name, or <see langword="null" /> when the canonical name is used.</summary>
    public string? FileSystemName { get; }

    /// <summary>Gets the language category.</summary>
    public LanguageType Type { get; }

    /// <summary>Gets whether Linguist marks the language as popular.</summary>
    public bool IsPopular { get; }

    /// <summary>Gets whether rendered source should wrap long lines by default.</summary>
    public bool WrapLines { get; }

    /// <summary>Gets the suggested hexadecimal display color, or <see langword="null" /> when unspecified.</summary>
    public string? Color { get; }

    /// <summary>Gets the TextMate scope, for example <c>source.cs</c>.</summary>
    public string TextMateScope { get; }

    /// <summary>Gets the Ace editor mode, or <see langword="null" /> when unspecified.</summary>
    public string? AceMode { get; }

    /// <summary>Gets the CodeMirror mode, or <see langword="null" /> when unspecified.</summary>
    public string? CodeMirrorMode { get; }

    /// <summary>Gets the CodeMirror MIME type, or <see langword="null" /> when unspecified.</summary>
    public string? CodeMirrorMimeType { get; }

    /// <summary>Gets the aliases accepted by Linguist for this language.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>Gets the filename extensions registered for this language.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the shebang interpreter names registered for this language.</summary>
    public IReadOnlyList<string> Interpreters { get; }

    /// <summary>Gets the exact special filenames registered for this language.</summary>
    public IReadOnlyList<string> Filenames { get; }

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
}

/// <summary>Describes the candidate languages produced by one detection strategy.</summary>
/// <example>An extension trace for <c>example.h</c> may contain C, C++, and Objective-C candidates.</example>
public sealed class StrategyTraceEntry
{
    internal StrategyTraceEntry(DetectionStrategy strategy, IEnumerable<LinguistLanguage> candidates)
    {
        Strategy = strategy;
        Candidates = Array.AsReadOnly(candidates.ToArray());
    }

    /// <summary>Gets the strategy that produced the candidate set.</summary>
    public DetectionStrategy Strategy { get; }

    /// <summary>Gets a copied read-only list of candidate languages.</summary>
    public IReadOnlyList<LinguistLanguage> Candidates { get; }
}

/// <summary>Pairs a classified language with its similarity score.</summary>
/// <remarks>The score is a similarity value, not a probability or confidence percentage.</remarks>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/classifier.rb#L116-L149" />
public sealed class ClassificationResult
{
    internal ClassificationResult(LinguistLanguage language, double score)
    {
        Language = language;
        Score = score;
    }

    /// <summary>Gets the classified language.</summary>
    public LinguistLanguage Language { get; }

    /// <summary>Gets the classifier similarity score.</summary>
    public double Score { get; }
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
    internal ClassificationResults(int consideredBytes, IEnumerable<ClassificationResult> results)
    {
        ConsideredBytes = consideredBytes;
        Results = Array.AsReadOnly(results.ToArray());
    }

    /// <summary>Gets the number of leading input bytes considered by the classifier.</summary>
    public int ConsideredBytes { get; }

    /// <summary>Gets classifier matches ordered from highest to lowest similarity score.</summary>
    public IReadOnlyList<ClassificationResult> Results { get; }
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
    internal BlobAnalysis(
        LinguistLanguage? language,
        DetectionStrategy strategy,
        bool isEmpty,
        BlobResultFlags flags,
        string mimeType,
        string contentType,
        string disposition,
        string? encoding,
        string? rubyEncoding,
        string? textMateScope,
        ulong? lineCount,
        ulong? sourceLineCount,
        IEnumerable<StrategyTraceEntry> strategyTrace)
    {
        if ((language is null) != (strategy == DetectionStrategy.None))
        {
            throw new LinguistException("A detected language and its selecting strategy must either both be present or both be absent.");
        }

        Language = language;
        Strategy = strategy;
        IsEmpty = isEmpty;
        Flags = flags;
        MimeType = mimeType;
        ContentType = contentType;
        Disposition = disposition;
        Encoding = encoding;
        RubyEncoding = rubyEncoding;
        TextMateScope = textMateScope;
        LineCount = lineCount;
        SourceLineCount = sourceLineCount;
        StrategyTrace = Array.AsReadOnly(strategyTrace.ToArray());
    }

    private BlobResultFlags Flags { get; }

    /// <summary>Gets the detected language, or <see langword="null" /> when Linguist found no language.</summary>
    public LinguistLanguage? Language { get; }

    /// <summary>Gets the strategy that selected the detected language.</summary>
    public DetectionStrategy Strategy { get; }

    /// <summary>Gets whether the supplied blob contained no bytes.</summary>
    public bool IsEmpty { get; }

    /// <summary>Gets whether Linguist's inexpensive initial checks consider the blob likely binary.</summary>
    public bool IsLikelyBinary => HasFlag(BlobResultFlags.LikelyBinary);

    /// <summary>Gets whether Linguist classifies the blob as binary.</summary>
    public bool IsBinary => HasFlag(BlobResultFlags.Binary);

    /// <summary>Gets whether Linguist classifies the blob as text.</summary>
    public bool IsText => HasFlag(BlobResultFlags.Text);

    /// <summary>Gets whether the blob is a recognized image format.</summary>
    public bool IsImage => HasFlag(BlobResultFlags.Image);

    /// <summary>Gets whether the blob is a recognized solid-model format.</summary>
    public bool IsSolidModel => HasFlag(BlobResultFlags.Solid);

    /// <summary>Gets whether the blob is recognized as comma-separated values.</summary>
    public bool IsCsv => HasFlag(BlobResultFlags.Csv);

    /// <summary>Gets whether the blob is a PDF document.</summary>
    public bool IsPdf => HasFlag(BlobResultFlags.Pdf);

    /// <summary>Gets whether Linguist considers the blob too large for normal rendering.</summary>
    public bool IsLarge => HasFlag(BlobResultFlags.Large);

    /// <summary>Gets whether GitHub-style rendering may display the blob.</summary>
    public bool IsViewable => HasFlag(BlobResultFlags.Viewable);

    /// <summary>Gets whether syntax colorization is safe for the blob.</summary>
    public bool IsSafeToColorize => HasFlag(BlobResultFlags.SafeToColorize);

    /// <summary>Gets whether an unusually high proportion of lines are very long.</summary>
    public bool HasHighRatioOfLongLines => HasFlag(BlobResultFlags.HighLongLineRatio);

    /// <summary>Gets whether the content is a Git LFS pointer.</summary>
    public bool IsLfsPointer => HasFlag(BlobResultFlags.LfsPointer);

    /// <summary>Gets whether the input path matches Linguist's vendored-code rules.</summary>
    public bool IsVendored => HasFlag(BlobResultFlags.Vendored);

    /// <summary>Gets whether the input path matches Linguist's documentation rules.</summary>
    public bool IsDocumentation => HasFlag(BlobResultFlags.Documentation);

    /// <summary>Gets whether Linguist considers the file generated.</summary>
    public bool IsGenerated => HasFlag(BlobResultFlags.Generated);

    /// <summary>Gets whether the blob is eligible for normal language detection.</summary>
    public bool IsDetectable => HasFlag(BlobResultFlags.Detectable);

    /// <summary>Gets whether the blob should contribute to repository language statistics.</summary>
    public bool IsIncludedInLanguageStatistics => HasFlag(BlobResultFlags.IncludeInStatistics);

    /// <summary>Gets the detected MIME type, for example <c>text/plain</c>.</summary>
    public string MimeType { get; }

    /// <summary>Gets the complete content type, potentially including a character set.</summary>
    public string ContentType { get; }

    /// <summary>Gets the suggested content disposition, for example <c>inline</c> or <c>attachment</c>.</summary>
    public string Disposition { get; }

    /// <summary>Gets the detected encoding name, or <see langword="null" /> when unavailable.</summary>
    public string? Encoding { get; }

    /// <summary>Gets the corresponding Ruby encoding name, or <see langword="null" /> when unavailable.</summary>
    public string? RubyEncoding { get; }

    /// <summary>Gets the detected language's TextMate scope, or <see langword="null" /> when no language was detected.</summary>
    public string? TextMateScope { get; }

    /// <summary>Gets the physical line count when requested, or <see langword="null" /> otherwise.</summary>
    public ulong? LineCount { get; }

    /// <summary>Gets the source-line count when requested, or <see langword="null" /> otherwise.</summary>
    public ulong? SourceLineCount { get; }

    /// <summary>Gets the ordered detection trace when requested, or an empty list otherwise.</summary>
    public IReadOnlyList<StrategyTraceEntry> StrategyTrace { get; }

    private bool HasFlag(BlobResultFlags flag) => (Flags & flag) != 0;
}
