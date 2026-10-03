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
        Assert.Throws<ObjectDisposedException>(() => runtime.Languages);
        Assert.Throws<ObjectDisposedException>(() => runtime.FindById(326));
        Assert.Throws<ObjectDisposedException>(() => runtime.FindByName("Ruby"));
        Assert.Throws<ObjectDisposedException>(() => runtime.FindByName(null!));
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
        BlobAnalysis analysis = runtime.Analyze("puts 'Hello'\n"u8, new BlobInput { Name = "hello.rb" });
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

        Task<BlobAnalysis> analysis = Task.Run(
            () => runtime.Analyze(data, new BlobInput { Name = "hello.rb" }),
            cancellationToken);
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
    public async Task ConcurrentDisposeCallsWaitForTheSameBackendRelease()
    {
        var backend = new FakeBackend(blockDispose: true);
        var runtime = new LinguistRuntime(backend);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task firstDispose = Task.Run(runtime.Dispose, cancellationToken);
        Assert.True(backend.DisposeEntered.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        Task secondDispose = Task.Run(runtime.Dispose, cancellationToken);

        await Task.Delay(50, cancellationToken);
        Assert.False(firstDispose.IsCompleted);
        Assert.False(secondDispose.IsCompleted);

        backend.ContinueDispose.Set();
        await Task.WhenAll(firstDispose, secondDispose);
        Assert.Equal(1, backend.DisposeCount);
    }

    [Fact]
    public void AnalyzePassesTypedBlobMetadataWithoutPositionalAmbiguity()
    {
        var backend = new FakeBackend();
        using var runtime = new LinguistRuntime(backend);
        var input = new BlobInput
        {
            Path = "src/Generated/Client.cs",
            Name = "Client.cs",
            IsSymlink = true,
            IsLfsTracked = true,
        };

        runtime.Analyze([], input);

        Assert.Same(input, backend.LastBlobInput);
    }

    [Fact]
    public void EmptyCandidateListReturnsNoResultsWithoutCallingTheClassifier()
    {
        var backend = new FakeBackend();
        using var runtime = new LinguistRuntime(backend);

        ClassificationResults results = runtime.Classify(
            "puts 'Hello'\n"u8,
            new ClassificationOptions { CandidateLanguageIds = [] });

        Assert.Equal(0, results.ConsideredBytes);
        Assert.Empty(results.Results);
        Assert.Equal(0, backend.ClassifyCount);
    }

    [Fact]
    public void UnknownCandidateLanguageIdIsRejectedBeforeNativeClassification()
    {
        var backend = new FakeBackend();
        using var runtime = new LinguistRuntime(backend);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => runtime.Classify(
            "puts 'Hello'\n"u8,
            new ClassificationOptions { CandidateLanguageIds = [327] }));

        Assert.Equal(nameof(ClassificationOptions.CandidateLanguageIds), exception.ParamName);
        Assert.Equal(0, backend.ClassifyCount);
    }

    [Fact]
    public void FindByIdUsesTheBackendIndex()
    {
        var backend = new FakeBackend();
        using var runtime = new LinguistRuntime(backend);

        Assert.Same(backend.Language, runtime.FindById(326));
        Assert.Null(runtime.FindById(1));
    }

    [Fact]
    public void ConsumersCanSubstituteTheRuntimeAndConstructResults()
    {
        var language = new LinguistLanguage { Id = 303, Name = "Python", Type = LanguageType.Programming, Extensions = [".py"] };
        var analysis = new BlobAnalysis { Language = language, Strategy = DetectionStrategy.Extension, IsText = true };
        var classification = new ClassificationResults
        {
            ConsideredBytes = 10,
            Results = [new ClassificationResult { Language = language, Score = 0.5 }],
        };
        var version = new LinguistVersionInfo { LinguistVersion = "9.6.0" };
        var trace = new StrategyTraceEntry { Strategy = DetectionStrategy.Extension, Candidates = [language] };

        Assert.Equal([".py"], language.Extensions);
        Assert.Same(language, analysis.Language);
        Assert.True(analysis.IsText);
        Assert.Empty(analysis.StrategyTrace);
        Assert.Same(language, Assert.Single(classification.Results).Language);
        Assert.Equal("9.6.0", version.LinguistVersion);
        Assert.Null(version.NativeBridgeRevision);
        Assert.Same(language, Assert.Single(trace.Candidates));
        Assert.True(typeof(ILinguistRuntime).IsAssignableFrom(typeof(LinguistRuntime)));
    }

    [Fact]
    public void LanguagesUseStableIdEquality()
    {
        var firstBackend = new FakeBackend();
        var secondBackend = new FakeBackend();

        Assert.NotSame(firstBackend.Language, secondBackend.Language);
        Assert.Equal(firstBackend.Language, secondBackend.Language);
        Assert.Equal(firstBackend.Language.GetHashCode(), secondBackend.Language.GetHashCode());
    }

    [Fact]
    public void PublicApiDoesNotExposeTheNativeLookupDiscriminator()
    {
        Assembly assembly = typeof(LinguistRuntime).Assembly;

        Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Name == "LanguageLookupKind");
        Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Name == "LinguistCapabilities");
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

        Assert.Equal(10, runtimeMethods.Length);
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
    public void DocumentationDistinguishesAnalysisFromClassification()
    {
        string documentationPath = Path.ChangeExtension(typeof(LinguistRuntime).Assembly.Location, ".xml");
        XDocument documentation = XDocument.Load(documentationPath);
        XElement analyze = Assert.Single(documentation.Descendants("member"), member =>
            ((string?)member.Attribute("name"))?.StartsWith(
                "M:MBW.GHLinguist.LinguistRuntime.Analyze",
                StringComparison.Ordinal) == true);

        Assert.Contains("not equivalent", analyze.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadmeContainsThePrimaryDecisionGuidance()
    {
        string readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "README.md"));

        Assert.Contains("Analyze(data)` is not equivalent to `Classify(data)", readme, StringComparison.Ordinal);
        Assert.Contains("FindByExtension", readme, StringComparison.Ordinal);
        Assert.Contains("explicit empty candidate list", readme, StringComparison.Ordinal);
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

    [Fact]
    public void NativeImportsUseTheGhlinguistAbi()
    {
        LibraryImportAttribute[] imports = typeof(NativeMethods)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .SelectMany(method => method.GetCustomAttributes<LibraryImportAttribute>())
            .ToArray();

        Assert.NotEmpty(imports);
        Assert.All(imports, import => Assert.Equal("ghlinguist", import.LibraryName));
        Assert.All(imports, import =>
        {
            Assert.NotNull(import.EntryPoint);
            Assert.StartsWith("ghl_", import.EntryPoint, StringComparison.Ordinal);
        });
    }

    private sealed class FakeBackend : ILinguistRuntimeBackend
    {
        private readonly bool _blockAnalysis;
        private readonly bool _blockDispose;

        internal FakeBackend(bool blockAnalysis = false, bool blockDispose = false)
        {
            _blockAnalysis = blockAnalysis;
            _blockDispose = blockDispose;
            Language = new LinguistLanguage
            {
                Id = 326,
                Name = "Ruby",
                Type = LanguageType.Programming,
                IsPopular = true,
                Color = "#701516",
                TextMateScope = "source.ruby",
                AceMode = "ruby",
                CodeMirrorMode = "ruby",
                CodeMirrorMimeType = "text/x-ruby",
                Aliases = ["ruby"],
                Extensions = [".rb"],
                Interpreters = ["ruby"],
                Filenames = ["Gemfile"],
            };
            Languages = Array.AsReadOnly([Language]);
            Version = new LinguistVersionInfo
            {
                AbiMajor = 1,
                PackageVersion = "1.0.0",
                RubyVersion = "4.0.6",
                LinguistVersion = "9.6.0",
                LinguistRevision = "196b2a1",
                ClassifierSha256 = "sha256",
            };
            Analysis = NativeLinguistRuntimeBackend.CreateAnalysis(
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
            Classification = new ClassificationResults
            {
                ConsideredBytes = 13,
                Results = [new ClassificationResult { Language = Language, Score = 0.9 }],
            };
        }

        internal int DisposeCount { get; private set; }

        internal int ClassifyCount { get; private set; }

        internal int AnalyzeCount { get; private set; }

        internal List<string> Lookups { get; } = [];

        internal ManualResetEventSlim AnalysisEntered { get; } = new();

        internal ManualResetEventSlim ContinueAnalysis { get; } = new();

        internal ManualResetEventSlim DisposeEntered { get; } = new();

        internal ManualResetEventSlim ContinueDispose { get; } = new();

        internal BlobInput? LastBlobInput { get; private set; }

        internal LinguistLanguage Language { get; }

        internal BlobAnalysis Analysis { get; }

        internal ClassificationResults Classification { get; }

        public LinguistVersionInfo Version { get; }

        public IReadOnlyList<LinguistLanguage> Languages { get; }

        public LinguistLanguage? FindById(ulong id) => id == Language.Id ? Language : null;

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

        public BlobAnalysis Analyze(ReadOnlySpan<byte> data, BlobInput input, BlobAnalysisOptions options)
        {
            AnalyzeCount++;
            LastBlobInput = input;
            if (_blockAnalysis)
            {
                AnalysisEntered.Set();
                ContinueAnalysis.Wait(TimeSpan.FromSeconds(5));
            }

            return Analysis;
        }

        public ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions options)
        {
            ClassifyCount++;
            return Classification;
        }

        public void Dispose()
        {
            DisposeCount++;
            if (_blockDispose)
            {
                DisposeEntered.Set();
                ContinueDispose.Wait(TimeSpan.FromSeconds(5));
            }
        }
    }
}
