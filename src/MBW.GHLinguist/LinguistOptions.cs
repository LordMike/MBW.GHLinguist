namespace MBW.GHLinguist;

/// <summary>Controls complete single-blob analysis.</summary>
/// <example>
/// Analyze an empty tracked file and request line counts and a strategy trace:
/// <code>
/// var options = new BlobAnalysisOptions
/// {
///     AllowEmpty = true,
///     IsLfsTracked = true,
///     IncludeLineCounts = true,
///     IncludeStrategyTrace = true,
/// };
/// </code>
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist.rb#L14-L72" />
public sealed record BlobAnalysisOptions
{
    private DetectionStrategyMask _strategies = DetectionStrategyMask.Default;

    /// <summary>Gets whether an empty blob may continue through language detection.</summary>
    /// <value><see langword="false" /> by default, matching <c>Linguist.detect</c>.</value>
    public bool AllowEmpty { get; init; }

    /// <summary>Gets whether the result includes each executed strategy and its candidates.</summary>
    public bool IncludeStrategyTrace { get; init; }

    /// <summary>Gets whether the result includes physical and source line counts.</summary>
    public bool IncludeLineCounts { get; init; }

    /// <summary>Gets whether Linguist should treat the input as a symbolic link.</summary>
    public bool IsSymlink { get; init; }

    /// <summary>Gets whether Git LFS explicitly tracks the input path.</summary>
    public bool IsLfsTracked { get; init; }

    /// <summary>Gets the detection strategies that may execute.</summary>
    /// <value><see cref="DetectionStrategyMask.Default" /> by default.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value contains an unsupported strategy bit.</exception>
    public DetectionStrategyMask Strategies
    {
        get => _strategies;
        init
        {
            if ((value & ~DetectionStrategyMask.Default) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The strategy mask contains unsupported values.");
            }

            _strategies = value;
        }
    }
}

/// <summary>Controls direct content classification independently of path-based detection.</summary>
/// <example>
/// Classify at most the first 16 KiB as a programming or markup language:
/// <code>
/// var options = new ClassificationOptions
/// {
///     AllowedTypes = LanguageTypeMask.Programming | LanguageTypeMask.Markup,
///     MaximumBytes = 16 * 1024,
/// };
/// </code>
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/classifier.rb#L91-L149" />
public sealed record ClassificationOptions
{
    /// <summary>The maximum prefix accepted by Linguist's classifier, in bytes.</summary>
    public const int DefaultMaximumBytes = 50 * 1024;

    private LanguageTypeMask _allowedTypes = LanguageTypeMask.All;
    private int _maximumBytes = DefaultMaximumBytes;
    private IReadOnlyList<ulong>? _candidateLanguageIds;

    /// <summary>Gets the language categories admitted as classifier results.</summary>
    /// <value><see cref="LanguageTypeMask.All" /> by default.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value contains an unsupported language-type bit.</exception>
    public LanguageTypeMask AllowedTypes
    {
        get => _allowedTypes;
        init
        {
            if ((value & ~LanguageTypeMask.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The language type mask contains unsupported values.");
            }

            _allowedTypes = value;
        }
    }

    /// <summary>Gets the maximum number of input bytes considered by the classifier.</summary>
    /// <value><c>51200</c> bytes by default.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one or greater than 51200.</exception>
    public int MaximumBytes
    {
        get => _maximumBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, DefaultMaximumBytes);
            _maximumBytes = value;
        }
    }

    /// <summary>Gets an optional set of language IDs to which classification is restricted.</summary>
    /// <value><see langword="null" /> for unrestricted classification, or a copied read-only list of IDs.</value>
    public IReadOnlyList<ulong>? CandidateLanguageIds
    {
        get => _candidateLanguageIds;
        init => _candidateLanguageIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
