using System.Text;

namespace MBW.GHLinguist.Tests;

public sealed class NativeRuntimeIntegrationTests
{
    private const string LinguistRevision = "196b2a14418cab005065c72c9759370934c184bc";
    private const string ClassifierSha256 = "24af803786a1157cb36a59feb5b4f2f3341a034ef7b5edd5b762a6d6ccb5d95d";

    [Fact]
    public void PackagedRuntimeExecutesThePublicManagedSurface()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GHL_RUN_NATIVE_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string nativeLibrary = OperatingSystem.IsWindows() ? "ghlinguist.dll" : "ghlinguist.so";
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, nativeLibrary)));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "provenance.json")));

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
        Assert.Equal(5UL, analysis.LineCount);
        Assert.NotEmpty(analysis.StrategyTrace);
        Assert.Contains(analysis.StrategyTrace, entry => entry.Strategy == DetectionStrategy.Extension && entry.Candidates.Contains(ruby));

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

        Assert.Throws<ArgumentException>(() => runtime.Classify(
            source,
            new ClassificationOptions { CandidateLanguageIds = [ulong.MaxValue - 1] }));
    }
}
