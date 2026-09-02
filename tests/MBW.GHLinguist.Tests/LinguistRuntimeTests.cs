using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace MBW.GHLinguist.Tests;

public sealed class LinguistRuntimeTests
{
    [Fact]
    public void DisposeIsIdempotentAndReleasesTheBackendOnce()
    {
        var backend = new FakeBackend();
        var runtime = new LinguistRuntime(backend);

        runtime.Dispose();
        runtime.Dispose();

        Assert.Equal(1, backend.DisposeCount);
    }

    [Fact]
    public void EveryStateDependentMemberThrowsAfterDisposal()
    {
        var runtime = new LinguistRuntime(new FakeBackend());
        runtime.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runtime.Version);
        Assert.Throws<ObjectDisposedException>(() => runtime.Capabilities);
        Assert.Throws<ObjectDisposedException>(() => runtime.Languages);
        Assert.Throws<ObjectDisposedException>(() => runtime.FindByName("Ruby"));
        Assert.Throws<ObjectDisposedException>(() => runtime.FindByAlias("ruby"));
        Assert.Throws<ObjectDisposedException>(() => runtime.FindByFilename("Gemfile"));
        Assert.Throws<ObjectDisposedException>(() => runtime.FindByExtension("example.rb"));
        Assert.Throws<ObjectDisposedException>(() => runtime.FindByInterpreter("ruby"));
        Assert.Throws<ObjectDisposedException>(() => runtime.Analyze([]));
        Assert.Throws<ObjectDisposedException>(() => runtime.Classify([]));
    }

    [Fact]
    public void LookupMethodsPreserveRubyReturnShapesAndDispatch()
    {
        var backend = new FakeBackend();
        using var runtime = new LinguistRuntime(backend);

        Assert.Same(backend.Language, runtime.FindByName("Ruby"));
        Assert.Same(backend.Language, runtime.FindByAlias("ruby"));
        Assert.Equal([backend.Language], runtime.FindByFilename("Gemfile"));
        Assert.Equal([backend.Language], runtime.FindByExtension("example.rb"));
        Assert.Equal([backend.Language], runtime.FindByInterpreter("ruby"));
        Assert.Equal(
            ["name:Ruby", "alias:ruby", "filename:Gemfile", "extension:example.rb", "interpreter:ruby"],
            backend.Lookups);
    }

    [Fact]
    public void ResultsCopiedBeforeDisposalRemainUsable()
    {
        var backend = new FakeBackend();
        var runtime = new LinguistRuntime(backend);

        LinguistVersionInfo version = runtime.Version;
        IReadOnlyList<LinguistLanguage> languages = runtime.Languages;
        BlobAnalysis analysis = runtime.Analyze("puts 'Hello'\n"u8, name: "hello.rb");
        ClassificationResults classification = runtime.Classify("puts 'Hello'\n"u8);
        runtime.Dispose();

        Assert.Equal("9.6.0", version.LinguistVersion);
        Assert.Equal("Ruby", Assert.Single(languages).Name);
        Assert.Equal("Ruby", analysis.Language?.Name);
        Assert.Equal("Ruby", Assert.Single(classification.Results).Language.Name);
    }

    [Fact]
    public async Task DisposalWaitsForAnActiveOperation()
    {
        var backend = new FakeBackend(blockAnalysis: true);
        var runtime = new LinguistRuntime(backend);
        byte[] data = "puts 'Hello'\n"u8.ToArray();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<BlobAnalysis> analysis = Task.Run(() => runtime.Analyze(data, name: "hello.rb"), cancellationToken);
        Assert.True(backend.AnalysisEntered.Wait(TimeSpan.FromSeconds(5), cancellationToken));

        Task dispose = Task.Run(runtime.Dispose, cancellationToken);
        await Task.Delay(50, cancellationToken);
        Assert.False(dispose.IsCompleted);

        backend.ContinueAnalysis.Set();
        Assert.Same(backend.Analysis, await analysis);
        await dispose;

        Assert.Equal(1, backend.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => runtime.Analyze(data));
    }

    [Fact]
    public void PublicApiDoesNotExposeTheNativeLookupDiscriminator()
    {
        Assembly assembly = typeof(LinguistRuntime).Assembly;

        Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Name == "LanguageLookupKind");
        Assert.Equal(typeof(LinguistLanguage), typeof(LinguistRuntime).GetMethod(nameof(LinguistRuntime.FindByName))?.ReturnType);
        Assert.Equal(typeof(LinguistLanguage), typeof(LinguistRuntime).GetMethod(nameof(LinguistRuntime.FindByAlias))?.ReturnType);
        Assert.Equal(typeof(IReadOnlyList<LinguistLanguage>), typeof(LinguistRuntime).GetMethod(nameof(LinguistRuntime.FindByFilename))?.ReturnType);
    }

    [Fact]
    public void GeneratedXmlDocumentationContainsRuntimeMethodsAndOriginalSources()
    {
        string documentationPath = Path.ChangeExtension(typeof(LinguistRuntime).Assembly.Location, ".xml");
        XDocument documentation = XDocument.Load(documentationPath);
        XElement[] runtimeMethods = documentation.Descendants("member")
            .Where(member => ((string?)member.Attribute("name"))?.StartsWith(
                "M:MBW.GHLinguist.LinguistRuntime.",
                StringComparison.Ordinal) == true)
            .ToArray();
        string[] memberNames = runtimeMethods
            .Select(member => (string)member.Attribute("name")!)
            .ToArray();

        Assert.Equal(9, runtimeMethods.Length);
        Assert.All(runtimeMethods, member => Assert.NotNull(member.Element("summary")));
        Assert.All(runtimeMethods, member => Assert.NotNull(member.Element("example")));
        Assert.All(
            runtimeMethods.Where(member => !((string)member.Attribute("name")!).EndsWith(".Dispose", StringComparison.Ordinal)),
            member => Assert.NotNull(member.Element("returns")));
        Assert.Contains(memberNames, name => name.StartsWith("M:MBW.GHLinguist.LinguistRuntime.Create", StringComparison.Ordinal));
        Assert.Contains(memberNames, name => name.StartsWith("M:MBW.GHLinguist.LinguistRuntime.FindByName", StringComparison.Ordinal));
        Assert.Contains(memberNames, name => name.StartsWith("M:MBW.GHLinguist.LinguistRuntime.FindByExtension", StringComparison.Ordinal));
        Assert.Contains(memberNames, name => name.StartsWith("M:MBW.GHLinguist.LinguistRuntime.Analyze", StringComparison.Ordinal));
        Assert.Contains(memberNames, name => name.StartsWith("M:MBW.GHLinguist.LinguistRuntime.Dispose", StringComparison.Ordinal));
        Assert.Contains("github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc", documentation.ToString());
    }

    [Fact]
    public void NativeInteropLayoutsMatchTheX64CAbi()
    {
        Assert.Equal(16, Unsafe.SizeOf<NativeStringView>());
        Assert.Equal(56, Unsafe.SizeOf<NativeRuntimeOptions>());
        Assert.Equal(88, Unsafe.SizeOf<NativeBlobInput>());
        Assert.Equal(48, Unsafe.SizeOf<NativeAnalysisOptions>());
        Assert.Equal(64, Unsafe.SizeOf<NativeClassifyOptions>());
        Assert.Equal(128, Unsafe.SizeOf<NativeVersionInfo>());
        Assert.Equal(192, Unsafe.SizeOf<NativeLanguageInfo>());
        Assert.Equal(48, Unsafe.SizeOf<NativeStrategyTraceEntry>());
        Assert.True(typeof(SafeHandle).IsAssignableFrom(typeof(NativeRuntimeHandle)));
        Assert.True(typeof(SafeHandle).IsAssignableFrom(typeof(NativeAnalysisHandle)));
        Assert.True(typeof(SafeHandle).IsAssignableFrom(typeof(NativeClassificationHandle)));
        Assert.True(typeof(SafeHandle).IsAssignableFrom(typeof(NativeLanguageIdListHandle)));
        Assert.True(typeof(SafeHandle).IsAssignableFrom(typeof(NativeErrorHandle)));
    }

    private sealed class FakeBackend : ILinguistRuntimeBackend
    {
        private readonly bool _blockAnalysis;

        internal FakeBackend(bool blockAnalysis = false)
        {
            _blockAnalysis = blockAnalysis;
            Language = new LinguistLanguage(
                326,
                null,
                "Ruby",
                null,
                LanguageType.Programming,
                isPopular: true,
                wrapLines: false,
                "#701516",
                "source.ruby",
                "ruby",
                "ruby",
                "text/x-ruby",
                ["ruby"],
                [".rb"],
                ["ruby"],
                ["Gemfile"]);
            Languages = Array.AsReadOnly([Language]);
            Version = new LinguistVersionInfo(1, 0, "1.0.0", "4.0.1", "9.6.0", "196b2a1", "sha256");
            Analysis = new BlobAnalysis(
                Language,
                DetectionStrategy.Extension,
                isEmpty: false,
                BlobResultFlags.Text | BlobResultFlags.Detectable,
                "text/plain",
                "text/plain; charset=utf-8",
                "inline",
                "UTF-8",
                "UTF-8",
                "source.ruby",
                1,
                1,
                []);
            Classification = new ClassificationResults(13, [new ClassificationResult(Language, 0.9)]);
        }

        internal int DisposeCount { get; private set; }

        internal List<string> Lookups { get; } = [];

        internal ManualResetEventSlim AnalysisEntered { get; } = new();

        internal ManualResetEventSlim ContinueAnalysis { get; } = new();

        internal LinguistLanguage Language { get; }

        internal BlobAnalysis Analysis { get; }

        internal ClassificationResults Classification { get; }

        public LinguistVersionInfo Version { get; }

        public LinguistCapabilities Capabilities => LinguistCapabilities.LanguageRegistry;

        public IReadOnlyList<LinguistLanguage> Languages { get; }

        public LinguistLanguage? FindByName(string name)
        {
            Lookups.Add($"name:{name}");
            return Language;
        }

        public LinguistLanguage? FindByAlias(string alias)
        {
            Lookups.Add($"alias:{alias}");
            return Language;
        }

        public IReadOnlyList<LinguistLanguage> FindByFilename(string filename)
        {
            Lookups.Add($"filename:{filename}");
            return Languages;
        }

        public IReadOnlyList<LinguistLanguage> FindByExtension(string filename)
        {
            Lookups.Add($"extension:{filename}");
            return Languages;
        }

        public IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter)
        {
            Lookups.Add($"interpreter:{interpreter}");
            return Languages;
        }

        public BlobAnalysis Analyze(ReadOnlySpan<byte> data, string? path, string? name, BlobAnalysisOptions options)
        {
            if (_blockAnalysis)
            {
                AnalysisEntered.Set();
                ContinueAnalysis.Wait(TimeSpan.FromSeconds(5));
            }

            return Analysis;
        }

        public ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions options) => Classification;

        public void Dispose() => DisposeCount++;
    }
}
