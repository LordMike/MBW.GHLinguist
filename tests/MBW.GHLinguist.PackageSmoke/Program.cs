using MBW.GHLinguist;

const string expectedRevision = "196b2a14418cab005065c72c9759370934c184bc";
const string expectedClassifierSha256 = "24af803786a1157cb36a59feb5b4f2f3341a034ef7b5edd5b762a6d6ccb5d95d";
string nativeLibrary = OperatingSystem.IsWindows() ? "ghlinguist.dll" : "ghlinguist.so";
string nativeAssetRoot = Path.Combine(AppContext.BaseDirectory, "MBW.GHLinguist");

Require(File.Exists(Path.Combine(nativeAssetRoot, nativeLibrary)), $"Missing packaged native library {nativeLibrary}.");
Require(Directory.Exists(Path.Combine(nativeAssetRoot, "lib", "ruby")), "Missing packaged Ruby standard library.");
Require(File.Exists(Path.Combine(nativeAssetRoot, "provenance.json")), "Missing packaged provenance manifest.");

LinguistRuntime runtime = LinguistRuntime.Create();
Require(runtime.Version.RubyVersion == "4.0.6", $"Unexpected Ruby version {runtime.Version.RubyVersion}.");
Require(runtime.Version.LinguistVersion == "9.6.0", $"Unexpected Linguist version {runtime.Version.LinguistVersion}.");
Require(runtime.Version.LinguistRevision == expectedRevision, $"Unexpected Linguist revision {runtime.Version.LinguistRevision}.");
Require(runtime.Version.LinguistRevision.Length == 40 && runtime.Version.LinguistRevision.All(Uri.IsHexDigit), "The packaged Linguist revision is not a Git SHA.");
Require(runtime.Version.ClassifierSha256 == expectedClassifierSha256, $"Unexpected classifier digest {runtime.Version.ClassifierSha256}.");
string rubyVersion = runtime.Version.RubyVersion;
string linguistVersion = runtime.Version.LinguistVersion;

LinguistLanguage ruby = runtime.FindByName("Ruby") ?? throw new InvalidOperationException("Ruby is missing from the packaged language registry.");
BlobAnalysis analysis = runtime.Analyze("puts 'package smoke'\n"u8, new BlobInput { Name = "smoke.rb" });
Require(analysis.Language == ruby, $"Expected Ruby analysis, found {analysis.Language?.Name ?? "none"}.");

BlobAnalysis pathOnly = runtime.Analyze("puts 'package smoke'\n"u8, new BlobInput { Path = "src/path-only.rb" });
Require(pathOnly.Language == ruby, "A null Name did not fall back to Path.");
BlobAnalysis nameOverride = runtime.Analyze("puts 'package smoke'\n"u8, new BlobInput { Path = "src/not-ruby.txt", Name = "override.rb" });
Require(nameOverride.Language == ruby, "Name did not override Path.");
BlobAnalysis emptyName = runtime.Analyze("puts 'package smoke'\n"u8, new BlobInput { Path = "src/empty-name.rb", Name = "" });
Require(emptyName.Language is null, "An empty Name was treated as an omitted Name.");

ClassificationResults classification = runtime.Classify(
    "class PackageSmoke\n  def value = 42\nend\n"u8,
    new ClassificationOptions { CandidateLanguageIds = [ruby.Id] });
Require(classification.Results.Count == 1 && classification.Results[0].Language == ruby, "The packaged classifier did not return Ruby.");

runtime.Dispose();
Require(ruby.Name == "Ruby", "Language results were not copied before runtime disposal.");
Require(analysis.Language == ruby, "Analysis results were not copied before runtime disposal.");
Require(pathOnly.Language == ruby, "Nullable path-only analysis was not copied before runtime disposal.");

Console.WriteLine($"Validated MBW.GHLinguist package with Ruby {rubyVersion} and Linguist {linguistVersion}.");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
