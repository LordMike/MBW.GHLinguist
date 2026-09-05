using System.Text;

namespace MBW.GHLinguist.Tests;

public sealed class NativeRuntimeIntegrationTests
{
    private const string LinguistRevision = "196b2a14418cab005065c72c9759370934c184bc";
    private const string ClassifierSha256 = "24af803786a1157cb36a59feb5b4f2f3341a034ef7b5edd5b762a6d6ccb5d95d";

    [Fact]
    public async Task PackagedRuntimeExecutesThePublicManagedSurface()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GHL_RUN_NATIVE_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string nativeLibrary = OperatingSystem.IsWindows() ? "ghlinguist.dll" : "ghlinguist.so";
        string assetRoot = Path.Combine(AppContext.BaseDirectory, "MBW.GHLinguist");
        Assert.True(File.Exists(Path.Combine(assetRoot, nativeLibrary)));
        Assert.True(File.Exists(Path.Combine(assetRoot, "provenance.json")));

        using LinguistRuntime runtime = LinguistRuntime.Create();

        Assert.Equal(1U, runtime.Version.AbiMajor);
        Assert.Equal(0U, runtime.Version.AbiMinor);
        Assert.Equal("4.0.6", runtime.Version.RubyVersion);
        Assert.Equal("9.6.0", runtime.Version.LinguistVersion);
        Assert.Equal(LinguistRevision, runtime.Version.LinguistRevision);
        Assert.Equal(ClassifierSha256, runtime.Version.ClassifierSha256);

        const LinguistCapabilities expectedCapabilities =
            LinguistCapabilities.LanguageRegistry |
            LinguistCapabilities.StandardDetection |
            LinguistCapabilities.ContentClassifier |
            LinguistCapabilities.StrategyTrace |
            LinguistCapabilities.EncodingAndBinaryDetection |
            LinguistCapabilities.GeneratedDetection |
            LinguistCapabilities.PathClassification;
        Assert.Equal(expectedCapabilities, runtime.Capabilities);
        Assert.True(runtime.Languages.Count > 700);

        BlobAnalysis empty = runtime.Analyze([]);
        Assert.Null(empty.Language);
        Assert.Equal(DetectionStrategy.None, empty.Strategy);
        Assert.True(empty.IsEmpty);
        Assert.Null(empty.TextMateScope);

        LinguistLanguage firstEnterprise = Assert.IsType<LinguistLanguage>(runtime.FindByName("1C Enterprise"));
        Assert.Equal(0UL, firstEnterprise.Id);
        Assert.Null(firstEnterprise.GroupLanguageId);

        LinguistLanguage ruby = Assert.IsType<LinguistLanguage>(runtime.FindByName("Ruby"));
        Assert.Equal(LanguageType.Programming, ruby.Type);
        Assert.Contains("ruby", ruby.Aliases);
        Assert.Equal(ruby, runtime.FindByAlias("ruby"));
        Assert.Contains(ruby, runtime.FindByFilename("Gemfile"));
        Assert.Contains(ruby, runtime.FindByExtension("example.rb"));
        Assert.Contains(ruby, runtime.FindByInterpreter("ruby"));
        Assert.Contains(runtime.Languages, language => language.Color is null);

        byte[] source = "class Greeter\n  def hello\n    puts 'hello'\n  end\nend\n"u8.ToArray();
        BlobAnalysis analysis = runtime.Analyze(
            source,
            new BlobInput { Path = "src/greeter.rb", Name = "greeter.rb" },
            new BlobAnalysisOptions { IncludeLineCounts = true, IncludeStrategyTrace = true });

        Assert.Equal(ruby, analysis.Language);
        Assert.Equal(DetectionStrategy.Extension, analysis.Strategy);
        Assert.True(analysis.IsText);
        Assert.True(analysis.IsDetectable);
        Assert.True(analysis.IsIncludedInLanguageStatistics);
        Assert.Equal("application/x-ruby", analysis.MimeType);
        Assert.Equal("inline", analysis.Disposition);
        Assert.Equal("ISO-8859-1", analysis.Encoding);
        Assert.Equal(5UL, analysis.LineCount);
        Assert.NotEmpty(analysis.StrategyTrace);
        Assert.Contains(analysis.StrategyTrace, entry => entry.Strategy == DetectionStrategy.Extension && entry.Candidates.Contains(ruby));

        BlobAnalysis pathOnly = runtime.Analyze(
            source,
            new BlobInput { Path = "src/path-only.rb" },
            new BlobAnalysisOptions { Strategies = DetectionStrategyMask.Extension });
        Assert.Equal(ruby, pathOnly.Language);

        BlobAnalysis emptyName = runtime.Analyze(
            source,
            new BlobInput { Path = "src/empty-name.rb", Name = "" },
            new BlobAnalysisOptions { Strategies = DetectionStrategyMask.Extension });
        Assert.Null(emptyName.Language);

        BlobAnalysis filename = runtime.Analyze(
            "source :rubygems\n"u8,
            new BlobInput { Path = "Gemfile", Name = "Gemfile" });
        Assert.Equal(ruby, filename.Language);
        Assert.Equal(DetectionStrategy.Filename, filename.Strategy);

        LinguistLanguage python = Assert.IsType<LinguistLanguage>(runtime.FindByName("Python"));
        BlobAnalysis shebang = runtime.Analyze(
            "#!/usr/bin/env python3\nprint(42)\n"u8,
            new BlobInput { Path = "script", Name = "script" });
        Assert.Equal(python, shebang.Language);
        Assert.Equal(DetectionStrategy.Shebang, shebang.Strategy);

        BlobAnalysis modeline = runtime.Analyze(
            "# -*- mode: ruby -*-\nputs :ok\n"u8,
            new BlobInput { Path = "source", Name = "source" });
        Assert.Equal(ruby, modeline.Language);
        Assert.Equal(DetectionStrategy.Modeline, modeline.Strategy);

        LinguistLanguage objectiveC = Assert.IsType<LinguistLanguage>(runtime.FindByName("Objective-C"));
        BlobAnalysis heuristic = runtime.Analyze(
            "#import <Foundation/Foundation.h>\n@interface Example : NSObject\n@end\n"u8,
            new BlobInput { Path = "src/example.h", Name = "example.h" });
        Assert.Equal(objectiveC, heuristic.Language);
        Assert.Equal(DetectionStrategy.Heuristics, heuristic.Strategy);

        LinguistLanguage xml = Assert.IsType<LinguistLanguage>(runtime.FindByName("XML"));
        BlobAnalysis xmlDeclaration = runtime.Analyze(
            "<?xml version=\"1.0\"?><project><name>demo</name></project>\n"u8,
            new BlobInput { Path = "source", Name = "source" });
        Assert.Equal(xml, xmlDeclaration.Language);
        Assert.Equal(DetectionStrategy.Xml, xmlDeclaration.Strategy);

        BlobAnalysis vendored = runtime.Analyze(
            "puts :ok\n"u8,
            new BlobInput { Path = "vendor/example.rb", Name = "example.rb" });
        Assert.Equal(ruby, vendored.Language);
        Assert.True(vendored.IsVendored);
        Assert.False(vendored.IsIncludedInLanguageStatistics);

        LinguistLanguage markdown = Assert.IsType<LinguistLanguage>(runtime.FindByName("Markdown"));
        BlobAnalysis documentation = runtime.Analyze(
            "# Documentation\n"u8,
            new BlobInput { Path = "docs/readme.md", Name = "readme.md" });
        Assert.Equal(markdown, documentation.Language);
        Assert.True(documentation.IsDocumentation);
        Assert.False(documentation.IsIncludedInLanguageStatistics);

        LinguistLanguage csharp = Assert.IsType<LinguistLanguage>(runtime.FindByName("C#"));
        BlobAnalysis generated = runtime.Analyze(
            "// <auto-generated />\npartial class Form1 {}\n"u8,
            new BlobInput { Path = "src/Form1.Designer.cs", Name = "Form1.Designer.cs" });
        Assert.Equal(csharp, generated.Language);
        Assert.True(generated.IsGenerated);
        Assert.False(generated.IsIncludedInLanguageStatistics);

        BlobAnalysis binary = runtime.Analyze(
            [0x61, 0x62, 0x63, 0x00, 0x64, 0x65, 0x66],
            new BlobInput { Path = "data.bin", Name = "data.bin" });
        Assert.Null(binary.Language);
        Assert.True(binary.IsBinary);
        Assert.False(binary.IsDetectable);

        BlobAnalysis lfsPointer = runtime.Analyze(
            "version https://git-lfs.github.com/spec/v1\noid sha256:0000000000000000000000000000000000000000000000000000000000000000\nsize 123\n"u8,
            new BlobInput { Path = "asset.png", Name = "asset.png", IsLfsTracked = true });
        Assert.True(lfsPointer.IsLfsPointer);
        Assert.False(lfsPointer.IsIncludedInLanguageStatistics);

        ClassificationResults classification = runtime.Classify(
            source,
            new ClassificationOptions { CandidateLanguageIds = [ruby.Id] });
        Assert.Equal(source.Length, classification.ConsideredBytes);
        Assert.Equal(ruby, Assert.Single(classification.Results).Language);

        byte[] classifierSource = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
            "class Greeter\n  def initialize(name)\n    @name = name\n  end\n  def hello\n    puts \"Hello #{@name}\"\n  end\nend\n",
            20)));
        ClassificationResults ranked = runtime.Classify(
            classifierSource,
            new ClassificationOptions { CandidateLanguageIds = [ruby.Id, python.Id] });
        Assert.Equal(classifierSource.Length, ranked.ConsideredBytes);
        Assert.Collection(
            ranked.Results,
            result =>
            {
                Assert.Equal(ruby, result.Language);
                Assert.Equal(0.35462628853032, result.Score, 12);
            },
            result =>
            {
                Assert.Equal(python, result.Language);
                Assert.Equal(0.15510731503460715, result.Score, 12);
            });

        LinguistLanguage javascript = Assert.IsType<LinguistLanguage>(runtime.FindByName("JavaScript"));
        LinguistLanguage json = Assert.IsType<LinguistLanguage>(runtime.FindByName("JSON"));
        LinguistLanguage yaml = Assert.IsType<LinguistLanguage>(runtime.FindByName("YAML"));
        ulong[] contentCandidateIds = [csharp.Id, python.Id, javascript.Id, json.Id, xml.Id, yaml.Id];
        (LinguistLanguage Expected, string Source)[] contentCases =
        [
            (csharp, "using System;\nnamespace Demo { public sealed class Greeter { public void Hello() { Console.WriteLine(\"hello\"); } } }\n"),
            (python, "def greet(name):\n    message = f\"Hello {name}\"\n    print(message)\n\nfor item in [\"a\", \"b\"]:\n    greet(item)\n"),
            (javascript, "export function greet(name) {\n  const message = `Hello ${name}`;\n  console.log(message);\n}\n[\"a\", \"b\"].forEach(greet);\n"),
            (json, "{\n  \"name\": \"demo\",\n  \"enabled\": true,\n  \"items\": [1, 2, 3],\n  \"metadata\": { \"owner\": \"team\" }\n}\n"),
            (xml, "<?xml version=\"1.0\"?>\n<project><name>demo</name><items><item id=\"1\">one</item></items></project>\n"),
            (yaml, "name: demo\nenabled: true\nitems:\n  - one\n  - two\nmetadata:\n  owner: team\n"),
            (csharp, "using System;\npublic class Broken {\n  public static void Main(string[] args) {\n    Console.WriteLine(\"unfinished\"\n"),
        ];
        foreach ((LinguistLanguage expected, string sample) in contentCases)
        {
            byte[] repeatedSource = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(sample, 8)));
            ClassificationResults contentClassification = runtime.Classify(
                repeatedSource,
                new ClassificationOptions { CandidateLanguageIds = contentCandidateIds });
            Assert.Equal(expected, contentClassification.Results[0].Language);
        }

        LinguistLanguage html = Assert.IsType<LinguistLanguage>(runtime.FindByName("HTML"));
        (LanguageTypeMask Mask, LinguistLanguage Expected, string Source)[] typeCases =
        [
            (LanguageTypeMask.Programming, ruby, "class Demo\n  def run\n    puts :ok\n  end\nend\n"),
            (LanguageTypeMask.Markup, html, "<!doctype html><html><head><title>Demo</title></head><body><main><h1>Hello</h1></main></body></html>\n"),
            (LanguageTypeMask.Data, json, "{\"name\":\"demo\",\"enabled\":true,\"items\":[1,2,3]}\n"),
            (LanguageTypeMask.Prose, markdown, "# Project Guide\n\nThis document explains how to install and configure the application.\n\n## Usage\n\nRun the command and review the output.\n"),
        ];
        ulong[] typeCandidateIds = [ruby.Id, html.Id, json.Id, markdown.Id];
        foreach ((LanguageTypeMask mask, LinguistLanguage expected, string sample) in typeCases)
        {
            byte[] repeatedSource = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(sample, 10)));
            ClassificationResults filtered = runtime.Classify(
                repeatedSource,
                new ClassificationOptions { AllowedTypes = mask, CandidateLanguageIds = typeCandidateIds });
            Assert.Equal(expected, Assert.Single(filtered.Results).Language);
        }

        ClassificationResults noCandidates = runtime.Classify(
            source,
            new ClassificationOptions { CandidateLanguageIds = [] });
        Assert.Equal(0, noCandidates.ConsideredBytes);
        Assert.Empty(noCandidates.Results);

        byte[] oversizedSource = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("puts 'Hello'\n", 5000)));
        ClassificationResults bounded = runtime.Classify(
            oversizedSource,
            new ClassificationOptions { CandidateLanguageIds = [ruby.Id] });
        Assert.Equal(ClassificationOptions.DefaultMaximumBytes, bounded.ConsideredBytes);

        Task<BlobAnalysis>[] concurrentCalls = Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => runtime.Analyze(
                source,
                new BlobInput { Path = $"src/concurrent-{index}.rb", Name = $"concurrent-{index}.rb" })))
            .ToArray();
        BlobAnalysis[] concurrentResults = await Task.WhenAll(concurrentCalls);
        Assert.All(concurrentResults, result => Assert.Equal(ruby, result.Language));

        for (int index = 0; index < 50; index++)
        {
            Assert.Equal(ruby, runtime.Analyze(source, new BlobInput { Name = $"repeated-{index}.rb" }).Language);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using (LinguistRuntime sharedRuntime = LinguistRuntime.Create())
        {
            Assert.Equal(runtime.Version.ClassifierSha256, sharedRuntime.Version.ClassifierSha256);
            Assert.Equal(ruby, sharedRuntime.Analyze(source, new BlobInput { Name = "shared.rb" }).Language);
        }

        LinguistRuntime disposableRuntime = LinguistRuntime.Create();
        LinguistLanguage copiedLanguage = Assert.IsType<LinguistLanguage>(disposableRuntime.FindByName("Ruby"));
        BlobAnalysis copiedAnalysis = disposableRuntime.Analyze(source, new BlobInput { Name = "copied.rb" });
        disposableRuntime.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposableRuntime.Analyze(source));
        Assert.Equal("Ruby", copiedLanguage.Name);
        Assert.Equal(copiedLanguage, copiedAnalysis.Language);

        Assert.Throws<ArgumentException>(() => runtime.Classify(
            source,
            new ClassificationOptions { CandidateLanguageIds = [ulong.MaxValue - 1] }));
    }
}
