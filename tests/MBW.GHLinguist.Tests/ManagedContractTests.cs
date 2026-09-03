namespace MBW.GHLinguist.Tests;

public sealed class ManagedContractTests
{
    [Fact]
    public void EnumValuesMatchTheNativeAbi()
    {
        Assert.Equal(4U, (uint)LanguageType.Prose);
        Assert.Equal(0x0fU, (uint)LanguageTypeMask.All);
        Assert.Equal(5U, (uint)LanguageLookupKind.Interpreter);
        Assert.Equal(8U, (uint)DetectionStrategy.Classifier);
        Assert.Equal(0xffU, (uint)DetectionStrategyMask.Default);
        Assert.Equal(1UL << 6, (ulong)LinguistCapabilities.PathClassification);
    }

    [Fact]
    public void OptionsUseLinguistDefaults()
    {
        BlobAnalysisOptions analysis = new();
        ClassificationOptions classification = new();

        Assert.Equal(DetectionStrategyMask.Default, analysis.Strategies);
        Assert.Equal(LanguageTypeMask.All, classification.AllowedTypes);
        Assert.Equal(50 * 1024, classification.MaximumBytes);
        Assert.Null(classification.CandidateLanguageIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50 * 1024 + 1)]
    public void ClassificationMaximumBytesMustBeWithinTheLinguistLimit(int maximumBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClassificationOptions
        {
            MaximumBytes = maximumBytes,
        });
    }

    [Fact]
    public void OptionMasksRejectUnknownAbiBits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlobAnalysisOptions
        {
            Strategies = (DetectionStrategyMask)(1U << 8),
        });

        Assert.Throws<ArgumentOutOfRangeException>(() => new ClassificationOptions
        {
            AllowedTypes = (LanguageTypeMask)(1U << 4),
        });
    }

    [Fact]
    public void LanguageCopiesNativeCollections()
    {
        string[] aliases = ["csharp", "c#"];
        LinguistLanguage language = CreateLanguage(aliases);

        aliases[0] = "changed";

        Assert.Equal(["csharp", "c#"], language.Aliases);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)language.Aliases)[0] = "changed again");
        Assert.Equal("C#", language.ToString());
    }

    [Fact]
    public void ClassificationOptionsCopyCandidateLanguageIds()
    {
        ulong[] candidateLanguageIds = [1, 2];
        ClassificationOptions options = new()
        {
            CandidateLanguageIds = candidateLanguageIds,
        };

        candidateLanguageIds[0] = 3;

        Assert.Equal([1UL, 2UL], options.CandidateLanguageIds);
    }

    [Fact]
    public void ClassificationOptionsPreserveAnExplicitEmptyCandidateList()
    {
        ClassificationOptions options = new()
        {
            CandidateLanguageIds = [],
        };

        Assert.NotNull(options.CandidateLanguageIds);
        Assert.Empty(options.CandidateLanguageIds);
    }

    [Fact]
    public void ClassificationOptionsRejectInvalidCandidateLanguageIds()
    {
        ArgumentOutOfRangeException sentinel = Assert.Throws<ArgumentOutOfRangeException>(() => new ClassificationOptions
        {
            CandidateLanguageIds = [1, ulong.MaxValue],
        });
        ArgumentException duplicate = Assert.Throws<ArgumentException>(() => new ClassificationOptions
        {
            CandidateLanguageIds = [1, 1],
        });

        Assert.Equal(nameof(ClassificationOptions.CandidateLanguageIds), sentinel.ParamName);
        Assert.Equal(nameof(ClassificationOptions.CandidateLanguageIds), duplicate.ParamName);
    }

    [Fact]
    public void ClassificationOptionsAllowLanguageIdZero()
    {
        var options = new ClassificationOptions { CandidateLanguageIds = [0] };

        Assert.Equal([0UL], options.CandidateLanguageIds);
    }

    [Fact]
    public void BlobInputDefaultsToContentOnlyAnalysis()
    {
        BlobInput input = new();

        Assert.Null(input.Path);
        Assert.Null(input.Name);
        Assert.False(input.IsSymlink);
        Assert.False(input.IsLfsTracked);
    }

    [Fact]
    public void BlobAnalysisProjectsNativeResultFlags()
    {
        BlobAnalysis analysis = new(
            CreateLanguage([]),
            DetectionStrategy.Extension,
            isEmpty: false,
            BlobResultFlags.Text | BlobResultFlags.Generated | BlobResultFlags.Detectable,
            "text/plain",
            "text/plain; charset=utf-8",
            "inline",
            "UTF-8",
            "UTF-8",
            "source.cs",
            12,
            10,
            []);

        Assert.True(analysis.IsText);
        Assert.True(analysis.IsGenerated);
        Assert.True(analysis.IsDetectable);
        Assert.False(analysis.IsBinary);
        Assert.Equal(12UL, analysis.LineCount);
        Assert.Equal(10UL, analysis.SourceLineCount);
    }

    [Fact]
    public void BlobAnalysisRejectsMismatchedLanguageAndStrategyState()
    {
        Assert.Throws<LinguistException>(() => new BlobAnalysis(
            CreateLanguage([]),
            DetectionStrategy.None,
            isEmpty: false,
            BlobResultFlags.Text,
            "text/plain",
            "text/plain; charset=utf-8",
            "inline",
            "UTF-8",
            "UTF-8",
            "source.cs",
            null,
            null,
            []));
    }

    private static LinguistLanguage CreateLanguage(IEnumerable<string> aliases) => new(
        42,
        null,
        "C#",
        "CSharp",
        LanguageType.Programming,
        isPopular: true,
        wrapLines: false,
        "#178600",
        "source.cs",
        "csharp",
        "clike",
        "text/x-csharp",
        aliases,
        [".cs"],
        [],
        []);
}
