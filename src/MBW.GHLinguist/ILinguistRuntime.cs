namespace MBW.GHLinguist;

/// <summary>Analyzes blobs and queries GitHub Linguist's language registry.</summary>
/// <remarks>
/// <see cref="LinguistRuntime" /> is the implementation. Depend on this interface to substitute the runtime in tests;
/// every result type has public init-only properties so test doubles can construct results without loading the
/// native runtime. The interface does not include <see cref="IDisposable" />: the owner of the
/// <see cref="LinguistRuntime" /> instance, such as a dependency-injection container, disposes it.
/// </remarks>
/// <example>
/// <code>
/// services.AddSingleton&lt;ILinguistRuntime&gt;(_ =&gt; LinguistRuntime.Create());
/// </code>
/// </example>
public interface ILinguistRuntime
{
    /// <inheritdoc cref="LinguistRuntime.Version" />
    LinguistVersionInfo Version { get; }

    /// <inheritdoc cref="LinguistRuntime.Languages" />
    IReadOnlyList<LinguistLanguage> Languages { get; }

    /// <inheritdoc cref="LinguistRuntime.FindById(ulong)" />
    LinguistLanguage? FindById(ulong id);

    /// <inheritdoc cref="LinguistRuntime.FindByName(string)" />
    LinguistLanguage? FindByName(string name);

    /// <inheritdoc cref="LinguistRuntime.FindByAlias(string)" />
    LinguistLanguage? FindByAlias(string alias);

    /// <inheritdoc cref="LinguistRuntime.FindByFilename(string)" />
    IReadOnlyList<LinguistLanguage> FindByFilename(string filenameOrPath);

    /// <inheritdoc cref="LinguistRuntime.FindByExtension(string)" />
    IReadOnlyList<LinguistLanguage> FindByExtension(string filenameOrPath);

    /// <inheritdoc cref="LinguistRuntime.FindByInterpreter(string)" />
    IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter);

    /// <inheritdoc cref="LinguistRuntime.Analyze(ReadOnlySpan{byte}, BlobInput?, BlobAnalysisOptions?)" />
    BlobAnalysis Analyze(ReadOnlySpan<byte> data, BlobInput? input = null, BlobAnalysisOptions? options = null);

    /// <inheritdoc cref="LinguistRuntime.Classify(ReadOnlySpan{byte}, ClassificationOptions?)" />
    ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions? options = null);
}
