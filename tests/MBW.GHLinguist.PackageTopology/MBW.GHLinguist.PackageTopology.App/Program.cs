using MBW.GHLinguist;
using MBW.GHLinguist.PackageTopology.Library;

using LinguistRuntime runtime = LinguistConsumer.CreateRuntime();
LinguistLanguage ruby = runtime.FindByName("Ruby") ?? throw new InvalidOperationException("Ruby is missing from the packaged language registry.");
BlobAnalysis analysis = runtime.Analyze("puts 'package topology'\n"u8, new BlobInput { Name = "topology.rb" });

if (analysis.Language != ruby)
{
    throw new InvalidOperationException($"Expected Ruby analysis, found {analysis.Language?.Name ?? "none"}.");
}

Console.WriteLine("Validated package consumption through a RID-neutral class library.");
