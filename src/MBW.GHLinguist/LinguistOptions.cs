namespace MBW.GHLinguist;

/// <summary>Provides the filename and repository metadata associated with blob bytes.</summary>
/// <remarks>
/// <see cref="Path" /> affects path-sensitive generated, vendored, documentation, and statistics rules.
/// <see cref="Name" /> affects filename and extension detection. When <see cref="Name" /> is
/// <see langword="null" />, the native bridge derives the filename from <see cref="Path" /> when possible.
/// Empty strings are passed to Linguist unchanged and are not treated as <see langword="null" />.
/// </remarks>
/// <example>
/// <code>
/// var input = new BlobInput
/// {
///     Path = "src/Generated/Client.cs",
///     Name = "Client.cs",
///     IsLfsTracked = false,
/// };
/// </code>
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/blob_helper.rb" />
public sealed class BlobInput
{
    /// <summary>Gets the optional repository-relative path, preferably using forward slashes.</summary>
    public string? Path { get; init; }

    /// <summary>Gets the optional filename used by filename and extension detection.</summary>
    public string? Name { get; init; }

    /// <summary>Gets whether Linguist should treat the input as a symbolic link.</summary>
    public bool IsSymlink { get; init; }

    /// <summary>Gets whether Git LFS explicitly tracks the input path.</summary>
    public bool IsLfsTracked { get; init; }
}

/// <summary>Controls complete single-blob analysis.</summary>
/// <example>
/// Analyze an empty tracked file and request line counts and a strategy trace:
/// <code>
/// var options = new BlobAnalysisOptions
/// {
///     AllowEmpty = true,
///     IncludeLineCounts = true,
///     IncludeStrategyTrace = true,
/// };
/// </code>
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist.rb#L14-L72" />
public sealed class BlobAnalysisOptions
{
    private DetectionStrategyMask _strategies = DetectionStrategyMask.Default;

    /// <summary>Gets whether an empty blob may continue through language detection.</summary>
    /// <value><see langword="false" /> by default, matching <c>Linguist.detect</c>.</value>
    public bool AllowEmpty { get; init; }

    /// <summary>Gets whether the result includes each executed strategy and its candidates.</summary>
    public bool IncludeStrategyTrace { get; init; }

    /// <summary>Gets whether the result includes physical and source line counts.</summary>
    public bool IncludeLineCounts { get; init; }

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
public sealed class ClassificationOptions
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
    /// <value>
    /// <see langword="null" /> for unrestricted classification, an empty list to return no matches without invoking
    /// the classifier, or a copied read-only list of specific nonzero IDs.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">The collection contains language ID zero.</exception>
    /// <exception cref="ArgumentException">The collection contains duplicate language IDs.</exception>
    public IReadOnlyList<ulong>? CandidateLanguageIds
    {
        get => _candidateLanguageIds;
        init
        {
            if (value is null)
            {
                _candidateLanguageIds = null;
                return;
            }

            ulong[] copy = value.ToArray();
            if (copy.Contains(0UL))
            {
                throw new ArgumentOutOfRangeException(nameof(CandidateLanguageIds), "Language ID zero does not identify a Linguist language.");
            }

            if (copy.Distinct().Count() != copy.Length)
            {
                throw new ArgumentException("Candidate language IDs must not contain duplicates.", nameof(CandidateLanguageIds));
            }

            _candidateLanguageIds = Array.AsReadOnly(copy);
        }
    }
}
