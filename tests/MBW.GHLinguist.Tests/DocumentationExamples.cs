namespace MBW.GHLinguist.Tests;

internal static class DocumentationExamples
{
    internal static BlobAnalysis AnalyzeAFile(byte[] source)
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();

        return runtime.Analyze(
            source,
            new BlobInput
            {
                Path = "src/hello.rb",
                Name = "hello.rb",
            });
    }

    internal static ClassificationResults ClassifyForOneLanguage(byte[] source)
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        LinguistLanguage ruby = runtime.FindByName("Ruby")
            ?? throw new InvalidOperationException("Ruby is missing from the registry.");

        return runtime.Classify(
            source,
            new ClassificationOptions
            {
                CandidateLanguageIds = [ruby.Id],
                AllowedTypes = LanguageTypeMask.Programming,
                MaximumBytes = 16 * 1024,
            });
    }

    internal static BlobAnalysis AnalyzeWithoutClassifier(byte[] source)
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();

        return runtime.Analyze(
            source,
            options: new BlobAnalysisOptions
            {
                Strategies = DetectionStrategyMask.Default & ~DetectionStrategyMask.Classifier,
            });
    }

    internal static IReadOnlyList<LinguistLanguage> FindByExtension(LinguistRuntime runtime) =>
        runtime.FindByExtension("src/example.rb");
}
