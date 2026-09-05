using MBW.GHLinguist;
using MBW.GHLinguist.PackageTopology.Library;

using LinguistRuntime runtime = LinguistConsumer.CreateRuntime();
LinguistLanguage ruby = runtime.FindByName("Ruby") ?? throw new InvalidOperationException("Ruby is missing from the packaged language registry.");
BlobAnalysis analysis = runtime.Analyze("puts 'package topology'\n"u8, new BlobInput { Name = "topology.rb" });

if (analysis.Language != ruby)
{
    throw new InvalidOperationException($"Expected Ruby analysis, found {analysis.Language?.Name ?? "none"}.");
}

BlobAnalysis pathOnly = runtime.Analyze("puts 'package topology'\n"u8, new BlobInput { Path = "src/topology.rb" });
if (pathOnly.Language != ruby)
{
    throw new InvalidOperationException("A null Name did not fall back to Path through the RID-neutral library.");
}

BlobAnalysis nameOverride = runtime.Analyze("puts 'package topology'\n"u8, new BlobInput { Path = "src/not-ruby.txt", Name = "topology-override.rb" });
if (nameOverride.Language != ruby)
{
    throw new InvalidOperationException("Name did not override Path through the RID-neutral library.");
}

Console.WriteLine("Validated package consumption through a RID-neutral class library.");
