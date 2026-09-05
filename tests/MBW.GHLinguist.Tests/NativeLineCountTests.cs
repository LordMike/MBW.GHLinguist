namespace MBW.GHLinguist.Tests;

public sealed class NativeLineCountTests
{
    public static bool NativeIntegrationEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("GHL_RUN_NATIVE_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void LineCountsPreserveLinguistsRenderingAndNonblankSemantics()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        var input = new BlobInput { Path = "source.rb", Name = "source.rb" };
        var options = new BlobAnalysisOptions { IncludeLineCounts = true };

        BlobAnalysis text = runtime.Analyze("# comment only\n\nputs :ok\n"u8, input, options);
        Assert.Equal(3UL, text.LineCount);
        Assert.Equal(2UL, text.SourceLineCount);

        byte[] largeText = new byte[1024 * 1024 + 2];
        largeText.AsSpan().Fill((byte)'\n');
        BlobAnalysis large = runtime.Analyze(largeText, input, options);
        Assert.False(large.IsEmpty);
        Assert.True(large.IsLarge);
        Assert.Equal(0UL, large.LineCount);
        Assert.Equal(0UL, large.SourceLineCount);

        BlobAnalysis binary = runtime.Analyze("abc\0def"u8, input, options);
        Assert.True(binary.IsBinary);
        Assert.Equal(0UL, binary.LineCount);
        Assert.Equal(0UL, binary.SourceLineCount);

        BlobAnalysis omitted = runtime.Analyze("puts :ok\n"u8, input);
        Assert.Null(omitted.LineCount);
        Assert.Null(omitted.SourceLineCount);
    }
}
