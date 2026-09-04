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

        ClassificationResults classification = runtime.Classify(
            source,
            new ClassificationOptions { CandidateLanguageIds = [ruby.Id] });
        Assert.Equal(source.Length, classification.ConsideredBytes);
        Assert.Equal(ruby, Assert.Single(classification.Results).Language);

        Assert.Throws<ArgumentException>(() => runtime.Classify(
            source,
            new ClassificationOptions { CandidateLanguageIds = [ulong.MaxValue - 1] }));
    }
}
