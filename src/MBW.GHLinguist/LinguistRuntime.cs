using System.Diagnostics.CodeAnalysis;

namespace MBW.GHLinguist;

/// <summary>Owns a native GitHub Linguist runtime and exposes blob analysis and language-registry APIs.</summary>
/// <remarks>
/// Calls are synchronous and thread-safe. Ruby work is serialized process-wide, and runtime instances reuse the
/// same initialized native runtime. <see cref="ClassifyDotNet" /> uses no Ruby, so its calls run in parallel.
/// Disposal waits for an active call to finish. Dispose the runtime when it is no longer needed. Results returned
/// before disposal are immutable managed copies and remain usable afterward.
/// </remarks>
/// <example>
/// <code>
/// using LinguistRuntime runtime = LinguistRuntime.Create();
/// LinguistLanguage ruby = runtime.FindByName("Ruby");
/// BlobAnalysis analysis = runtime.Analyze(
///     "puts 'Hello'\n"u8,
///     new BlobInput { Path = "src/hello.rb", Name = "hello.rb" });
/// </code>
/// </example>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/docs/how-linguist-works.md" />
public sealed class LinguistRuntime : ILinguistRuntime, IDisposable
{
    private readonly object _gate = new();
    private ILinguistRuntimeBackend? _backend;

    internal LinguistRuntime(ILinguistRuntimeBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>Creates a runtime using deployment-local native Linguist assets for the current platform.</summary>
    /// <remarks>
    /// The complete closure must remain in the <c>MBW.GHLinguist</c> directory beside the managed assembly; use an
    /// isolated, complete output layout when validating native integration. Only x64 Windows and Linux processes are
    /// supported. The first creation that reaches CRuby startup fixes process-wide native state. If startup fails
    /// after CRuby initialization begins, repair the deployment and restart the process; retrying cannot recover it.
    /// </remarks>
    /// <returns>A live runtime. For example, its <see cref="Version" /> property reports the loaded Linguist revision.</returns>
    /// <exception cref="DllNotFoundException">The native runtime or one of its dependencies cannot be found.</exception>
    /// <exception cref="BadImageFormatException">A native asset targets the wrong architecture or platform.</exception>
    /// <exception cref="PlatformNotSupportedException">The process is not x64 Windows or Linux, or single-file deployment prevents locating the native asset directory.</exception>
    /// <exception cref="LinguistException">The deployed native closure fails integrity validation, lacks a required feature, or cannot initialize Linguist.</exception>
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

    /// <summary>Gets the complete copied GitHub Linguist language registry.</summary>
    /// <value>A read-only list containing entries such as Ruby, C#, and Markdown.</value>
    /// <remarks>The list retains Linguist's registry order. Use <see cref="LinguistLanguage.Id" /> for stable identity.</remarks>
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

    /// <summary>Gets a language by its stable numeric Linguist language ID.</summary>
    /// <param name="id">The language ID, for example from <see cref="LinguistLanguage.GroupLanguageId" />.</param>
    /// <returns>The matching language.</returns>
    /// <remarks>
    /// The lookup uses a cached index and does not scan <see cref="Languages" />. Use
    /// <see cref="TryFindById(ulong, out LinguistLanguage)" /> when the ID may be absent.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">The loaded registry has no language with <paramref name="id" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>LinguistLanguage group = runtime.FindById(language.GroupLanguageId!.Value);</code></example>
    public LinguistLanguage FindById(ulong id) =>
        TryFindById(id, out LinguistLanguage? language)
            ? language
            : throw new KeyNotFoundException($"The loaded Linguist registry has no language with ID {id}.");

    /// <summary>Tries to find a language by its stable numeric Linguist language ID.</summary>
    /// <param name="id">The language ID, for example from <see cref="LinguistLanguage.GroupLanguageId" />.</param>
    /// <param name="language">The matching language, or <see langword="null" /> when none matches.</param>
    /// <returns><see langword="true" /> when the loaded registry contains <paramref name="id" />; otherwise <see langword="false" />.</returns>
    /// <remarks>The lookup uses a cached index and does not scan <see cref="Languages" />.</remarks>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>if (runtime.TryFindById(id, out LinguistLanguage? language)) { Console.WriteLine(language.Name); }</code></example>
    public bool TryFindById(ulong id, [NotNullWhen(true)] out LinguistLanguage? language)
    {
        lock (_gate)
        {
            language = GetBackend().FindById(id);
            return language is not null;
        }
    }

    /// <summary>Gets a language by its canonical or filesystem name using Linguist's <c>find_by_name</c> semantics.</summary>
    /// <param name="name">The case-insensitive canonical or filesystem name, for example <c>Ruby</c>.</param>
    /// <returns>The matching language.</returns>
    /// <remarks>
    /// Whitespace is not trimmed, matching Linguist. Use <see cref="TryFindByName(string, out LinguistLanguage)" />
    /// when the name may be unknown, for example when it comes from user input.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="KeyNotFoundException">No language matches <paramref name="name" />, including an empty string.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>LinguistLanguage ruby = runtime.FindByName("ruby");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L103-L116" />
    public LinguistLanguage FindByName(string name) =>
        TryFindByName(name, out LinguistLanguage? language)
            ? language
            : throw new KeyNotFoundException($"No Linguist language has the name '{name}'.");

    /// <summary>Tries to find a language by its canonical or filesystem name using Linguist's <c>find_by_name</c> semantics.</summary>
    /// <param name="name">The case-insensitive canonical or filesystem name, for example <c>Ruby</c>.</param>
    /// <param name="language">The matching language, or <see langword="null" /> when none matches.</param>
    /// <returns><see langword="true" /> when a language matches; otherwise <see langword="false" />.</returns>
    /// <remarks>Empty strings return <see langword="false" />. Whitespace is not trimmed, matching Linguist.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>if (runtime.TryFindByName(userInput, out LinguistLanguage? language)) { Console.WriteLine(language.Id); }</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L103-L116" />
    public bool TryFindByName(string name, [NotNullWhen(true)] out LinguistLanguage? language)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(name);
            language = backend.FindByName(name);
            return language is not null;
        }
    }

    /// <summary>Gets a language by an alias using Linguist's <c>find_by_alias</c> semantics.</summary>
    /// <param name="alias">The case-insensitive alias, for example <c>cpp</c>.</param>
    /// <returns>The matching language.</returns>
    /// <remarks>
    /// Whitespace is not trimmed, matching Linguist. Use <see cref="TryFindByAlias(string, out LinguistLanguage)" />
    /// when the alias may be unknown.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="alias" /> is <see langword="null" />.</exception>
    /// <exception cref="KeyNotFoundException">No language matches <paramref name="alias" />, including an empty string.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>LinguistLanguage cpp = runtime.FindByAlias("cpp");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L118-L131" />
    public LinguistLanguage FindByAlias(string alias) =>
        TryFindByAlias(alias, out LinguistLanguage? language)
            ? language
            : throw new KeyNotFoundException($"No Linguist language has the alias '{alias}'.");

    /// <summary>Tries to find a language by an alias using Linguist's <c>find_by_alias</c> semantics.</summary>
    /// <param name="alias">The case-insensitive alias, for example <c>cpp</c>.</param>
    /// <param name="language">The matching language, or <see langword="null" /> when none matches.</param>
    /// <returns><see langword="true" /> when a language matches; otherwise <see langword="false" />.</returns>
    /// <remarks>Empty strings return <see langword="false" />. Whitespace is not trimmed, matching Linguist.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="alias" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>if (runtime.TryFindByAlias("cpp", out LinguistLanguage? cpp)) { Console.WriteLine(cpp.Name); }</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L118-L131" />
    public bool TryFindByAlias(string alias, [NotNullWhen(true)] out LinguistLanguage? language)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(alias);
            language = backend.FindByAlias(alias);
            return language is not null;
        }
    }

    /// <summary>Finds languages registered for an exact special filename using Linguist's <c>find_by_filename</c>.</summary>
    /// <param name="filenameOrPath">A filename or path whose basename is matched case-sensitively, for example <c>src/Cakefile</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>Cakefile</c> includes CoffeeScript. The list is empty when none match.</returns>
    /// <remarks>Linguist compares the basename case-sensitively and does not inspect ordinary file extensions here.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="filenameOrPath" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByFilename("Cakefile");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L133-L151" />
    public IReadOnlyList<LinguistLanguage> FindByFilename(string filenameOrPath)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(filenameOrPath);
            return backend.FindByFilename(filenameOrPath);
        }
    }

    /// <summary>Finds languages by the recognized extension of a filename using Linguist's <c>find_by_extension</c>.</summary>
    /// <param name="filenameOrPath">
    /// A filename or path such as <c>src/program.rb</c> or <c>.rb</c>, not a bare extension such as <c>rb</c>.
    /// </param>
    /// <returns>A read-only list of matches; for example, <c>program.rb</c> includes Ruby. The list is empty when none match.</returns>
    /// <remarks>
    /// Linguist lowercases the filename and considers recognized compound extensions in its own precedence order.
    /// A name without a dot, such as <c>rb</c> or <c>Makefile</c>, has no extension for Linguist to match and is
    /// rejected rather than silently returning no languages. Use <see cref="FindByFilename(string)" /> for special
    /// extensionless filenames.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="filenameOrPath" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The filename part of <paramref name="filenameOrPath" /> contains no dot, or <paramref name="filenameOrPath" /> contains invalid UTF-16.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <example><code>IReadOnlyList&lt;LinguistLanguage&gt; matches = runtime.FindByExtension("program.rb");</code></example>
    /// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb#L153-L175" />
    public IReadOnlyList<LinguistLanguage> FindByExtension(string filenameOrPath)
    {
        lock (_gate)
        {
            ILinguistRuntimeBackend backend = GetBackend();
            ArgumentNullException.ThrowIfNull(filenameOrPath);
            int filenameStart = filenameOrPath.LastIndexOfAny(['/', '\\']) + 1;
            if (filenameOrPath.IndexOf('.', filenameStart) < 0)
            {
                throw new ArgumentException(
                    $"'{filenameOrPath}' has no file extension. Pass a filename such as 'example.rb' or '.rb', not a bare extension such as 'rb'.",
                    nameof(filenameOrPath));
            }

            return backend.FindByExtension(filenameOrPath);
        }
    }

    /// <summary>Finds languages registered for an exact shebang interpreter using Linguist's <c>find_by_interpreter</c>.</summary>
    /// <param name="interpreter">The case-sensitive interpreter name, for example <c>bash</c>.</param>
    /// <returns>A read-only list of matches; for example, <c>bash</c> includes Shell. The list is empty when none match.</returns>
    /// <remarks>The interpreter lookup is case-sensitive and does not parse a complete shebang line.</remarks>
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
    /// <remarks>
    /// Calling this method without <paramref name="input" /> omits path and filename metadata, but still performs
    /// blob checks and runs the enabled analysis strategies. It is not equivalent to <see cref="Classify" />, which
    /// performs classifier-only ranking. Analysis copies the complete input and has no built-in size cap, streaming,
    /// or cancellation; callers processing untrusted input must apply their own limits before calling.
    /// </remarks>
    /// <param name="data">The complete blob bytes. They are copied for the native request during this synchronous call.</param>
    /// <param name="input">
    /// Optional filename and repository metadata. <see langword="null" /> analyzes the bytes without path or filename
    /// metadata while retaining the configured blob-analysis pipeline.
    /// </param>
    /// <param name="options">Optional analysis behavior; <see langword="null" /> uses Linguist defaults.</param>
    /// <returns>A copied result; for example, a <c>hello.rb</c> blob can report Ruby selected by <see cref="DetectionStrategy.Extension" />.</returns>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <exception cref="ArgumentException"><see cref="BlobInput.Path" /> or <see cref="BlobInput.Name" /> contains invalid UTF-16.</exception>
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
            return GetBackend().Analyze(data, input ?? new BlobInput(), options ?? new BlobAnalysisOptions());
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
            if (!ValidateClassification(backend, effectiveOptions))
            {
                return new ClassificationResults();
            }

            return backend.Classify(data, effectiveOptions);
        }
    }

    /// <summary>Classifies source content like <see cref="Classify" />, using a .NET port of Linguist's classifier instead of Ruby.</summary>
    /// <remarks>
    /// The port tokenizes with a transliteration of Linguist's flex tokenizer and scores against the same classifier
    /// database Linguist loads, performing every floating-point operation in Linguist's order, so rankings and scores
    /// match <see cref="Classify" /> bit for bit. It does not enter Ruby: calls take a fraction of a millisecond and
    /// concurrent calls run in parallel. The first call parses the classifier database. To classify without starting
    /// Ruby at all, use <see cref="LinguistClassifier" />.
    /// </remarks>
    /// <param name="data">Source bytes. At most the configured leading 50 KiB are considered.</param>
    /// <param name="options">Optional classifier filters and byte limit; <see langword="null" /> uses Linguist defaults.</param>
    /// <returns>Matches ordered by descending similarity, identical to <see cref="Classify" />.</returns>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <exception cref="ArgumentException">A candidate language ID is not present in this runtime's registry.</exception>
    /// <exception cref="LinguistException">The classifier database cannot be loaded, or the classifier produced a score outside 0 to 1 beyond rounding.</exception>
    /// <example><code>ClassificationResults results = runtime.ClassifyDotNet("class Example {}"u8);</code></example>
    /// <seealso cref="Classify(ReadOnlySpan{byte}, ClassificationOptions?)" />
    public ClassificationResults ClassifyDotNet(
        ReadOnlySpan<byte> data,
        ClassificationOptions? options = null)
    {
        ILinguistRuntimeBackend backend;
        ClassificationOptions effectiveOptions = options ?? new ClassificationOptions();
        lock (_gate)
        {
            backend = GetBackend();
            if (!ValidateClassification(backend, effectiveOptions))
            {
                return new ClassificationResults();
            }

            backend.PrepareDotNetClassifier();
        }

        // Scoring reads only immutable managed tables, so it runs outside the gate and in parallel.
        return backend.ClassifyDotNet(data, effectiveOptions);
    }

    /// <summary>Releases this runtime's native handle.</summary>
    /// <remarks>
    /// Calling this method more than once is safe. Concurrent disposal calls wait for the same native release to
    /// finish. Previously returned managed results remain usable. It does not unload CRuby, stop the process-wide
    /// worker, or release the process-wide Ruby runtime.
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

    // Returns false when an empty candidate list means there is nothing to classify.
    private static bool ValidateClassification(ILinguistRuntimeBackend backend, ClassificationOptions options)
    {
        if (options.CandidateLanguageIds is { Count: 0 })
        {
            return false;
        }

        if (options.CandidateLanguageIds is { } candidateLanguageIds)
        {
            foreach (ulong languageId in candidateLanguageIds)
            {
                if (backend.FindById(languageId) is null)
                {
                    throw new ArgumentException(
                        $"Candidate language ID {languageId} does not exist in the loaded Linguist registry.",
                        nameof(ClassificationOptions.CandidateLanguageIds));
                }
            }
        }

        return true;
    }

    private ILinguistRuntimeBackend GetBackend() =>
        _backend ?? throw new ObjectDisposedException(nameof(LinguistRuntime));
}

internal interface ILinguistRuntimeBackend : IDisposable
{
    LinguistVersionInfo Version { get; }

    IReadOnlyList<LinguistLanguage> Languages { get; }

    LinguistLanguage? FindById(ulong id);

    LinguistLanguage? FindByName(string name);

    LinguistLanguage? FindByAlias(string alias);

    IReadOnlyList<LinguistLanguage> FindByFilename(string filenameOrPath);

    IReadOnlyList<LinguistLanguage> FindByExtension(string filenameOrPath);

    IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter);

    BlobAnalysis Analyze(ReadOnlySpan<byte> data, BlobInput input, BlobAnalysisOptions options);

    ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions options);

    /// <summary>Loads what <see cref="ClassifyDotNet" /> needs; called under the runtime gate before each call.</summary>
    void PrepareDotNetClassifier();

    /// <summary>Classifies without the runtime gate, after <see cref="PrepareDotNetClassifier" />; must be thread-safe.</summary>
    ClassificationResults ClassifyDotNet(ReadOnlySpan<byte> data, ClassificationOptions options);
}
