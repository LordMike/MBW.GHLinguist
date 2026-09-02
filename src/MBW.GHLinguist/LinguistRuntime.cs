namespace MBW.GHLinguist;

/// <summary>Owns a native GitHub Linguist runtime and exposes blob analysis and language-registry APIs.</summary>
/// <remarks>
/// Calls are synchronous. Dispose the runtime when it is no longer needed. Results returned before disposal are
/// immutable managed copies and remain usable afterward.
/// </remarks>
/// <example>
/// <code>
/// using LinguistRuntime runtime = LinguistRuntime.Create();
/// LinguistLanguage? ruby = runtime.FindByName("Ruby");
/// BlobAnalysis analysis = runtime.Analyze("puts 'Hello'\n"u8, name: "hello.rb");
/// </code>
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/docs/how-linguist-works.md" />
public sealed class LinguistRuntime : IDisposable
{
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
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    public IReadOnlyList<LinguistLanguage> Languages
    {
        get
        {
            lock (_gate)
            {
                return GetBackend().Languages;
            }
        }
    }

    /// <summary>Finds a language by its canonical or filesystem name using Linguist's <c>find_by_name</c> semantics.</summary>
    /// <param name="name">The case-insensitive canonical or filesystem name, for example <c>Ruby</c>.</param>
    /// <returns>The matching language, or <see langword="null" /> when no language matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>LinguistLanguage? ruby = runtime.FindByName("ruby");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L103-L116" />
    public LinguistLanguage? FindByName(string name)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(name);
            return backend.FindByName(name);
        }
    }

    /// <summary>Finds a language by an alias using Linguist's <c>find_by_alias</c> semantics.</summary>
    /// <param name="alias">The case-insensitive alias, for example <c>cpp</c>.</param>
    /// <returns>The matching language, or <see langword="null" /> when no language matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="alias" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>LinguistLanguage? cpp = runtime.FindByAlias("cpp");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L118-L131" />
    public LinguistLanguage? FindByAlias(string alias)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(alias);
            return backend.FindByAlias(alias);
        }
    }

    /// <summary>Finds languages registered for an exact special filename using Linguist's <c>find_by_filename</c>.</summary>
    /// <param name="filename">A filename or path whose basename is matched case-sensitively, for example <c>src/Cakefile</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>Cakefile</c> includes CoffeeScript. The list is empty when none match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filename" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByFilename("Cakefile");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L133-L151" />
    public IReadOnlyList<LinguistLanguage> FindByFilename(string filename)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(filename);
            return backend.FindByFilename(filename);
        }
    }

    /// <summary>Finds languages by the recognized extension of a filename using Linguist's <c>find_by_extension</c>.</summary>
    /// <param name="filename">A filename or path such as <c>src/program.rb</c>, not a bare extension such as <c>rb</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>program.rb</c> includes Ruby. The list is empty when none match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filename" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByExtension("program.rb");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L153-L175" />
    public IReadOnlyList<LinguistLanguage> FindByExtension(string filename)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(filename);
            return backend.FindByExtension(filename);
        }
    }

    /// <summary>Finds languages registered for an exact shebang interpreter using Linguist's <c>find_by_interpreter</c>.</summary>
    /// <param name="interpreter">The case-sensitive interpreter name, for example <c>bash</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>bash</c> includes Shell. The list is empty when none match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="interpreter" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByInterpreter("bash");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L177-L189" />
    public IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(interpreter);
            return backend.FindByInterpreter(interpreter);
        }
    }

    /// <summary>Performs complete GitHub Linguist analysis of one blob.</summary>
    /// <param name="data">The complete blob bytes. The span is borrowed only for this synchronous call.</param>
    /// <param name="path">An optional repository-relative path used by generated, vendored, and documentation detection.</param>
    /// <param name="name">An optional display filename. When omitted, the runtime derives it from <paramref name="path" />.</param>
    /// <param name="options">Optional analysis behavior; <see langword="null" /> uses Linguist defaults.</param>
    /// <returns>A copied result; for example, a <c>hello.rb</c> blob can report Ruby selected by <see cref="DetectionStrategy.Extension" />.</returns>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="path" /> or <paramref name="name" /> contains invalid UTF-16.</exception>
    /// <exception cref="LinguistException">Linguist or the native runtime cannot analyze the blob.</exception>
    /// <example>
    /// <code>BlobAnalysis result = runtime.Analyze("puts 'Hello'\n"u8, name: "hello.rb");</code>
    /// </example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist.rb#L14-L72" />
    public BlobAnalysis Analyze(
        ReadOnlySpan<byte> data,
        string? path = null,
        string? name = null,
        BlobAnalysisOptions? options = null)
    {
        lock (_gate)
        {
            return GetBackend().Analyze(data, path, name, options ?? new BlobAnalysisOptions());
        }
    }

    /// <summary>Classifies source content directly without path-based detection strategies.</summary>
    /// <param name="data">Source bytes. At most the configured leading 50 KiB are considered.</param>
    /// <param name="options">Optional classifier filters and byte limit; <see langword="null" /> uses Linguist defaults.</param>
    /// <returns>Matches ordered by descending similarity; for example, C# may be first with a score near <c>0.9</c>.</returns>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <exception cref="LinguistException">Linguist or the native runtime cannot classify the content.</exception>
    /// <example><code>ClassificationResults results = runtime.Classify("class Example {}"u8);</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/classifier.rb#L91-L149" />
    public ClassificationResults Classify(
        ReadOnlySpan<byte> data,
        ClassificationOptions? options = null)
    {
        lock (_gate)
        {
            return GetBackend().Classify(data, options ?? new ClassificationOptions());
        }
    }

    /// <summary>Releases this runtime's native handle.</summary>
    /// <remarks>Calling this method more than once is safe. Previously returned managed results remain usable.</remarks>
    /// <example><code>runtime.Dispose();</code></example>
    public void Dispose()
    {
        ILinguistRuntimeBackend? backend;

        lock (_gate)
        {
            backend = _backend;
            _backend = null;
        }

        backend?.Dispose();
    }

    private ILinguistRuntimeBackend GetBackend() =>
        _backend ?? throw new ObjectDisposedException(nameof(LinguistRuntime));
}

internal interface ILinguistRuntimeBackend : IDisposable
{
    LinguistVersionInfo Version { get; }

    LinguistCapabilities Capabilities { get; }

    IReadOnlyList<LinguistLanguage> Languages { get; }

    LinguistLanguage? FindByName(string name);

    LinguistLanguage? FindByAlias(string alias);

    IReadOnlyList<LinguistLanguage> FindByFilename(string filename);

    IReadOnlyList<LinguistLanguage> FindByExtension(string filename);

    IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter);

    BlobAnalysis Analyze(ReadOnlySpan<byte> data, string? path, string? name, BlobAnalysisOptions options);

    ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions options);
}
