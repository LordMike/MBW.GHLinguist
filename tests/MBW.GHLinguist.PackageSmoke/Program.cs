using MBW.GHLinguist;

const string expectedRevision = "196b2a14418cab005065c72c9759370934c184bc";
const string expectedClassifierSha256 = "24af803786a1157cb36a59feb5b4f2f3341a034ef7b5edd5b762a6d6ccb5d95d";
string nativeLibrary = OperatingSystem.IsWindows() ? "ghlinguist.dll" : "ghlinguist.so";
string nativeAssetRoot = Path.Combine(AppContext.BaseDirectory, "MBW.GHLinguist");

Require(File.Exists(Path.Combine(nativeAssetRoot, nativeLibrary)), $"Missing packaged native library {nativeLibrary}.");
Require(Directory.Exists(Path.Combine(nativeAssetRoot, "lib", "ruby")), "Missing packaged Ruby standard library.");
Require(File.Exists(Path.Combine(nativeAssetRoot, "provenance.json")), "Missing packaged provenance manifest.");

using LinguistRuntime runtime = LinguistRuntime.Create();
Require(runtime.Version.RubyVersion == "4.0.6", $"Unexpected Ruby version {runtime.Version.RubyVersion}.");
Require(runtime.Version.LinguistVersion == "9.6.0", $"Unexpected Linguist version {runtime.Version.LinguistVersion}.");
Require(runtime.Version.LinguistRevision == expectedRevision, $"Unexpected Linguist revision {runtime.Version.LinguistRevision}.");
Require(runtime.Version.ClassifierSha256 == expectedClassifierSha256, $"Unexpected classifier digest {runtime.Version.ClassifierSha256}.");

LinguistLanguage ruby = runtime.FindByName("Ruby") ?? throw new InvalidOperationException("Ruby is missing from the packaged language registry.");
BlobAnalysis analysis = runtime.Analyze("puts 'package smoke'\n"u8, new BlobInput { Name = "smoke.rb" });
Require(analysis.Language == ruby, $"Expected Ruby analysis, found {analysis.Language?.Name ?? "none"}.");

ClassificationResults classification = runtime.Classify(
    "class PackageSmoke\n  def value = 42\nend\n"u8,
    new ClassificationOptions { CandidateLanguageIds = [ruby.Id] });
Require(classification.Results.Count == 1 && classification.Results[0].Language == ruby, "The packaged classifier did not return Ruby.");

Console.WriteLine($"Validated MBW.GHLinguist package with Ruby {runtime.Version.RubyVersion} and Linguist {runtime.Version.LinguistVersion}.");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
