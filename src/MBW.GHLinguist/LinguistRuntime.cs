namespace MBW.GHLinguist;

/// <summary>Owns a native GitHub Linguist runtime and exposes blob analysis and language-registry APIs.</summary>
/// <remarks>
/// Calls are synchronous, thread-safe, and mutually exclusive for each runtime instance. Disposal waits for an
/// active call to finish. Dispose the runtime when it is no longer needed. Results returned before disposal are
/// immutable managed copies and remain usable afterward.
/// </remarks>
/// <example>
/// <code>
/// using LinguistRuntime runtime = LinguistRuntime.Create();
/// LinguistLanguage? ruby = runtime.FindByName("Ruby");
/// BlobAnalysis analysis = runtime.Analyze(
///     "puts 'Hello'\n"u8,
///     new BlobInput { Path = "src/hello.rb", Name = "hello.rb" });
/// </code>
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/docs/how-linguist-works.md" />
public sealed class LinguistRuntime : IDisposable
{
    private const LinguistCapabilities CompleteAnalysisCapabilities =
        LinguistCapabilities.LanguageRegistry |
        LinguistCapabilities.StandardDetection |
        LinguistCapabilities.EncodingAndBinaryDetection |
        LinguistCapabilities.GeneratedDetection |
        LinguistCapabilities.PathClassification;

    private readonly object _gate = new();
    private ILinguistRuntimeBackend? _backend;

    internal LinguistRuntime(ILinguistRuntimeBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>Creates a runtime using the native Linguist assets for the current platform.</summary>
    /// <returns>A live runtime. For example, its <see cref="Version" /> property reports the loaded Linguist revision.</returns>
    /// <exception cref="DllNotFoundException">The native runtime or one of its dependencies cannot be found.</exception>
    /// <exception cref="BadImageFormatException">A native asset targets the wrong architecture or platform.</exception>
    /// <exception cref="LinguistException">The native runtime cannot initialize Linguist.</exception>
    /// <example>
    /// <code>using LinguistRuntime runtime = LinguistRuntime.Create();</code>
    /// </example>
    public static LinguistRuntime Create() => new(NativeLinguistRuntimeBackend.Create());

    /// <summary>Gets version and provenance information for the loaded runtime.</summary>
    /// <value>For example, a result can report Linguist <c>9.6.0</c> at its pinned Git revision.</value>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    public LinguistVersionInfo Version
    {
        get
        {
            lock (_gate)
            {
                return GetBackend().Version;
            }
        }
    }

    /// <summary>Gets the features implemented by the loaded native runtime.</summary>
    /// <value>A flags value such as <see cref="LinguistCapabilities.LanguageRegistry" />.</value>
    /// <remarks>
    /// Registry lookup requires <see cref="LinguistCapabilities.LanguageRegistry" />. Complete analysis additionally
    /// requires <see cref="LinguistCapabilities.StandardDetection" />,
    /// <see cref="LinguistCapabilities.EncodingAndBinaryDetection" />,
    /// <see cref="LinguistCapabilities.GeneratedDetection" />, and
    /// <see cref="LinguistCapabilities.PathClassification" />. Analysis also requires
    /// <see cref="LinguistCapabilities.StrategyTrace" /> or <see cref="LinguistCapabilities.ContentClassifier" />
    /// when the corresponding option or strategy is enabled. Direct classification requires language-registry and
    /// content-classifier capabilities, except for an explicit empty candidate list. Unsupported operations throw
    /// <see cref="NotSupportedException" /> before invoking the backend.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    public LinguistCapabilities Capabilities
    {
        get
        {
            lock (_gate)
            {
                return GetBackend().Capabilities;
            }
        }
    }

    /// <summary>Gets the complete copied GitHub Linguist language registry.</summary>
    /// <value>A read-only list containing entries such as Ruby, C#, and Markdown.</value>
    /// <remarks>The list retains Linguist's registry order. Use <see cref="LinguistLanguage.Id" /> for stable identity.</remarks>
    /// <exception cref="NotSupportedException">The loaded runtime does not provide language-registry access.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    public IReadOnlyList<LinguistLanguage> Languages
    {
        get
        {
            lock (_gate)
            {
                ILinguistRuntimeBackend backend = GetBackend();
                RequireCapabilities(backend, LinguistCapabilities.LanguageRegistry, nameof(Languages));
                return backend.Languages;
            }
        }
    }

    /// <summary>Finds a language by its canonical or filesystem name using Linguist's <c>find_by_name</c> semantics.</summary>
    /// <param name="name">The case-insensitive canonical or filesystem name, for example <c>Ruby</c>.</param>
    /// <returns>The matching language, or <see langword="null" /> when no language matches.</returns>
    /// <remarks>Empty strings return <see langword="null" />. Whitespace is not trimmed, matching Linguist.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">The loaded runtime does not provide language-registry access.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>LinguistLanguage? ruby = runtime.FindByName("ruby");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L103-L116" />
    public LinguistLanguage? FindByName(string name)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(name);
            RequireCapabilities(backend, LinguistCapabilities.LanguageRegistry, nameof(FindByName));
            return backend.FindByName(name);
        }
    }

    /// <summary>Finds a language by an alias using Linguist's <c>find_by_alias</c> semantics.</summary>
    /// <param name="alias">The case-insensitive alias, for example <c>cpp</c>.</param>
    /// <returns>The matching language, or <see langword="null" /> when no language matches.</returns>
    /// <remarks>Empty strings return <see langword="null" />. Whitespace is not trimmed, matching Linguist.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="alias" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">The loaded runtime does not provide language-registry access.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>LinguistLanguage? cpp = runtime.FindByAlias("cpp");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L118-L131" />
    public LinguistLanguage? FindByAlias(string alias)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(alias);
            RequireCapabilities(backend, LinguistCapabilities.LanguageRegistry, nameof(FindByAlias));
            return backend.FindByAlias(alias);
        }
    }

    /// <summary>Finds languages registered for an exact special filename using Linguist's <c>find_by_filename</c>.</summary>
    /// <param name="filenameOrPath">A filename or path whose basename is matched case-sensitively, for example <c>src/Cakefile</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>Cakefile</c> includes CoffeeScript. The list is empty when none match.</returns>
    /// <remarks>Linguist compares the basename case-sensitively and does not inspect ordinary file extensions here.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="filenameOrPath" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">The loaded runtime does not provide language-registry access.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByFilename("Cakefile");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L133-L151" />
    public IReadOnlyList<LinguistLanguage> FindByFilename(string filenameOrPath)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(filenameOrPath);
            RequireCapabilities(backend, LinguistCapabilities.LanguageRegistry, nameof(FindByFilename));
            return backend.FindByFilename(filenameOrPath);
        }
    }

    /// <summary>Finds languages by the recognized extension of a filename using Linguist's <c>find_by_extension</c>.</summary>
    /// <param name="filenameOrPath">A filename or path such as <c>src/program.rb</c>, not a bare extension such as <c>rb</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>program.rb</c> includes Ruby. The list is empty when none match.</returns>
    /// <remarks>Linguist lowercases the filename and considers recognized compound extensions in its own precedence order.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="filenameOrPath" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">The loaded runtime does not provide language-registry access.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByExtension("program.rb");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L153-L175" />
    public IReadOnlyList<LinguistLanguage> FindByExtension(string filenameOrPath)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(filenameOrPath);
            RequireCapabilities(backend, LinguistCapabilities.LanguageRegistry, nameof(FindByExtension));
            return backend.FindByExtension(filenameOrPath);
        }
    }

    /// <summary>Finds languages registered for an exact shebang interpreter using Linguist's <c>find_by_interpreter</c>.</summary>
    /// <param name="interpreter">The case-sensitive interpreter name, for example <c>bash</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>bash</c> includes Shell. The list is empty when none match.</returns>
    /// <remarks>The interpreter lookup is case-sensitive and does not parse a complete shebang line.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="interpreter" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">The loaded runtime does not provide language-registry access.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByInterpreter("bash");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L177-L189" />
    public IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(interpreter);
            RequireCapabilities(backend, LinguistCapabilities.LanguageRegistry, nameof(FindByInterpreter));
            return backend.FindByInterpreter(interpreter);
        }
    }

    /// <summary>Performs complete GitHub Linguist analysis of one blob.</summary>
    /// <remarks>
    /// Calling this method without <paramref name="input" /> omits path and filename metadata, but still performs
    /// blob checks and runs the enabled analysis strategies. It is not equivalent to <see cref="Classify" />, which
    /// performs classifier-only ranking.
    /// </remarks>
    /// <param name="data">The complete blob bytes. The span is borrowed only for this synchronous call.</param>
    /// <param name="input">
    /// Optional filename and repository metadata. <see langword="null" /> analyzes the bytes without path or filename
    /// metadata while retaining the configured blob-analysis pipeline.
    /// </param>
    /// <param name="options">Optional analysis behavior; <see langword="null" /> uses Linguist defaults.</param>
    /// <returns>A copied result; for example, a <c>hello.rb</c> blob can report Ruby selected by <see cref="DetectionStrategy.Extension" />.</returns>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <exception cref="ArgumentException"><see cref="BlobInput.Path" /> or <see cref="BlobInput.Name" /> contains invalid UTF-16.</exception>
    /// <exception cref="NotSupportedException">The loaded runtime lacks a capability required for complete blob analysis.</exception>
    /// <exception cref="LinguistException">Linguist or the native runtime cannot analyze the blob.</exception>
    /// <example>
    /// <code>
    /// BlobAnalysis result = runtime.Analyze(
    ///     "puts 'Hello'\n"u8,
    ///     new BlobInput { Path = "src/hello.rb", Name = "hello.rb" });
    /// </code>
    /// </example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist.rb#L14-L72" />
    /// <seealso cref="Classify(ReadOnlySpan{byte}, ClassificationOptions?)" />
    public BlobAnalysis Analyze(
        ReadOnlySpan<byte> data,
        BlobInput? input = null,
        BlobAnalysisOptions? options = null)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            BlobAnalysisOptions effectiveOptions = options ?? new BlobAnalysisOptions();
            LinguistCapabilities requiredCapabilities = CompleteAnalysisCapabilities;
            if (effectiveOptions.IncludeStrategyTrace)
            {
                requiredCapabilities |= LinguistCapabilities.StrategyTrace;
            }

            if ((effectiveOptions.Strategies & DetectionStrategyMask.Classifier) != 0)
            {
                requiredCapabilities |= LinguistCapabilities.ContentClassifier;
            }

            RequireCapabilities(backend, requiredCapabilities, nameof(Analyze));
            return backend.Analyze(data, input ?? new BlobInput(), effectiveOptions);
        }
    }

    /// <summary>Classifies source content directly without path-based detection strategies.</summary>
    /// <remarks>
    /// Use this method for classifier-only ranking. For normal Linguist file detection, including blob checks and
    /// ordered strategies, use <see cref="Analyze" /> instead.
    /// </remarks>
    /// <param name="data">Source bytes. At most the configured leading 50 KiB are considered.</param>
    /// <param name="options">Optional classifier filters and byte limit; <see langword="null" /> uses Linguist defaults.</param>
    /// <returns>Matches ordered by descending similarity; for example, C# may be first with a score near <c>0.9</c>.</returns>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <exception cref="ArgumentException">A candidate language ID is not present in this runtime's registry.</exception>
    /// <exception cref="NotSupportedException">The loaded runtime does not provide content classification or required registry access.</exception>
    /// <exception cref="LinguistException">Linguist or the native runtime cannot classify the content.</exception>
    /// <example><code>ClassificationResults results = runtime.Classify("class Example {}"u8);</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/classifier.rb#L91-L149" />
    /// <seealso cref="Analyze(ReadOnlySpan{byte}, BlobInput?, BlobAnalysisOptions?)" />
    public ClassificationResults Classify(
        ReadOnlySpan<byte> data,
        ClassificationOptions? options = null)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ClassificationOptions effectiveOptions = options ?? new ClassificationOptions();
            if (effectiveOptions.CandidateLanguageIds is { Count: 0 })
            {
                return new ClassificationResults(0, []);
            }

            RequireCapabilities(
                backend,
                LinguistCapabilities.ContentClassifier | LinguistCapabilities.LanguageRegistry,
                nameof(Classify));
            if (effectiveOptions.CandidateLanguageIds is { } candidateLanguageIds)
            {
                var knownLanguageIds = backend.Languages.Select(language => language.Id).ToHashSet();
                foreach (ulong languageId in candidateLanguageIds)
                {
                    if (!knownLanguageIds.Contains(languageId))
                    {
                        throw new ArgumentException(
                            $"Candidate language ID {languageId} does not exist in the loaded Linguist registry.",
                            nameof(ClassificationOptions.CandidateLanguageIds));
                    }
                }
            }

            return backend.Classify(data, effectiveOptions);
        }
    }

    /// <summary>Releases this runtime's native handle.</summary>
    /// <remarks>
    /// Calling this method more than once is safe. Concurrent disposal calls wait for the same native release to
    /// finish. Previously returned managed results remain usable.
    /// </remarks>
    /// <example><code>runtime.Dispose();</code></example>
    public void Dispose()
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend? backend = _backend;
            _backend = null;
            backend?.Dispose();
        }
    }

    private ILinguistRuntimeBackend GetBackend() =>
        _backend ?? throw new ObjectDisposedException(nameof(LinguistRuntime));

    private static void RequireCapabilities(
        ILinguistRuntimeBackend backend,
        LinguistCapabilities required,
        string operation)
    {
        LinguistCapabilities missing = required & ~backend.Capabilities;
        if (missing != LinguistCapabilities.None)
        {
            throw new NotSupportedException($"{operation} requires unavailable Linguist capabilities: {missing}.");
        }
    }
}

internal interface ILinguistRuntimeBackend : IDisposable
{
    LinguistVersionInfo Version { get; }

    LinguistCapabilities Capabilities { get; }

    IReadOnlyList<LinguistLanguage> Languages { get; }

    LinguistLanguage? FindByName(string name);

    LinguistLanguage? FindByAlias(string alias);

    IReadOnlyList<LinguistLanguage> FindByFilename(string filenameOrPath);

    IReadOnlyList<LinguistLanguage> FindByExtension(string filenameOrPath);

    IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter);

    BlobAnalysis Analyze(ReadOnlySpan<byte> data, BlobInput input, BlobAnalysisOptions options);

    ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions options);
}
