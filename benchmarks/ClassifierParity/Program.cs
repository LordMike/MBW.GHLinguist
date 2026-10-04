using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MBW.GHLinguist;

// Compares Linguist's Ruby classifier (LinguistRuntime.Classify) with the managed port
// (LinguistRuntime.ClassifyDotNet): full ranked lists must be identical, with bit-identical scores.
// Then times both paths, single-threaded and in parallel. Exit code 1 on any mismatch.

BenchOptions options = BenchOptions.Parse(args);
List<Case> cases = Corpus.Load(options);
using LinguistRuntime runtime = LinguistRuntime.Create();
Console.WriteLine($"Linguist {runtime.Version.LinguistVersion} ({runtime.Version.LinguistRevision[..Math.Min(12, runtime.Version.LinguistRevision.Length)]}), Ruby {runtime.Version.RubyVersion}, package {runtime.Version.PackageVersion}");
Console.WriteLine($"{Environment.ProcessorCount} cores, .NET {Environment.Version}, {RuntimeInformationText()}");

// First calls: each path loads its classifier lazily.
Case first = cases[0];
double rubyFirstMs = Time(() => runtime.Classify(first.Data, first.Options));
double managedFirstMs = Time(() => runtime.ClassifyDotNet(first.Data, first.Options));
Console.WriteLine($"First call: Ruby {rubyFirstMs:F1} ms, managed {managedFirstMs:F1} ms");

// Parity
var mismatches = new List<string>();
var bySource = new SortedDictionary<string, (int Cases, int Mismatches)>(StringComparer.Ordinal);
int topTwoMismatches = 0;
foreach (Case item in cases)
{
    ClassificationResults ruby = runtime.Classify(item.Data, item.Options);
    ClassificationResults managed = runtime.ClassifyDotNet(item.Data, item.Options);
    string? difference = Compare(ruby, managed, out bool topTwoDiffers);
    (int count, int bad) = bySource.GetValueOrDefault(item.Source);
    if (difference is not null)
    {
        bad++;
        if (topTwoDiffers)
        {
            topTwoMismatches++;
        }

        if (mismatches.Count < 20)
        {
            mismatches.Add($"{item.Name} [{item.OptionsName}]: {difference}");
        }
    }

    bySource[item.Source] = (count + 1, bad);
}

int totalMismatches = bySource.Values.Sum(v => v.Mismatches);
Console.WriteLine();
Console.WriteLine($"Parity over {cases.Count} cases: {totalMismatches} full-ranking mismatches, {topTwoMismatches} top-two mismatches");
foreach ((string source, (int count, int bad)) in bySource)
{
    Console.WriteLine($"  {source,-28} {count,7} cases {bad,5} mismatches");
}

foreach (string mismatch in mismatches)
{
    Console.WriteLine($"  MISMATCH {mismatch}");
}

// Timing
Console.WriteLine();
Console.WriteLine($"Timing over {cases.Count} cases, {options.Rounds} rounds (ms per call; mean of each round, then p50/p99 of the last round)");
var timings = new List<object>();
foreach ((string name, Func<Case, ClassificationResults> classify) in new (string, Func<Case, ClassificationResults>)[]
         {
             ("Ruby (Classify)", c => runtime.Classify(c.Data, c.Options)),
             ("Managed (ClassifyDotNet)", c => runtime.ClassifyDotNet(c.Data, c.Options)),
         })
{
    double[] perCall = new double[cases.Count];
    var roundMeans = new List<double>();
    for (int round = 0; round < options.Rounds; round++)
    {
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < cases.Count; index++)
        {
            long callStart = Stopwatch.GetTimestamp();
            classify(cases[index]);
            perCall[index] = Stopwatch.GetElapsedTime(callStart).TotalMilliseconds;
        }

        roundMeans.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds / cases.Count);
    }

    Array.Sort(perCall);
    var parallelMeans = new List<double>();
    for (int round = 0; round < options.Rounds; round++)
    {
        long start = Stopwatch.GetTimestamp();
        Parallel.For(0, cases.Count, new ParallelOptions { MaxDegreeOfParallelism = options.Threads }, index => classify(cases[index]));
        parallelMeans.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds / cases.Count);
    }

    double p50 = perCall[perCall.Length / 2], p99 = perCall[(int)(perCall.Length * 0.99)];
    double single = roundMeans.Min(), parallel = parallelMeans.Min();
    Console.WriteLine($"  {name,-26} single {Join(roundMeans)}  p50 {p50:F3}  p99 {p99:F3}  |  {options.Threads} threads {Join(parallelMeans)} ({1000 / parallel:F0} calls/s)");
    timings.Add(new { name, singleMsPerCall = roundMeans, p50, p99, threads = options.Threads, parallelWallMsPerCall = parallelMeans });
}

if (options.Output is not null)
{
    var report = new
    {
        linguist = runtime.Version.LinguistVersion,
        ruby = runtime.Version.RubyVersion,
        package = runtime.Version.PackageVersion,
        cores = Environment.ProcessorCount,
        cases = cases.Count,
        firstCallMs = new { ruby = rubyFirstMs, managed = managedFirstMs },
        parity = new { mismatches = totalMismatches, topTwoMismatches, bySource = bySource.ToDictionary(p => p.Key, p => new { cases = p.Value.Cases, mismatches = p.Value.Mismatches }), examples = mismatches },
        timings,
    };
    File.WriteAllText(options.Output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

return totalMismatches == 0 ? 0 : 1;

static string Join(IEnumerable<double> values) => "[" + string.Join(", ", values.Select(v => v.ToString("F3", CultureInfo.InvariantCulture))) + "]";

static double Time(Action action)
{
    long start = Stopwatch.GetTimestamp();
    action();
    return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}

static string RuntimeInformationText() => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

static string? Compare(ClassificationResults expected, ClassificationResults actual, out bool topTwoDiffers)
{
    topTwoDiffers = false;
    string? difference = null;
    if (expected.ConsideredBytes != actual.ConsideredBytes)
    {
        difference = $"consideredBytes {expected.ConsideredBytes} vs {actual.ConsideredBytes}";
    }

    int count = Math.Max(expected.Results.Count, actual.Results.Count);
    for (int index = 0; index < count; index++)
    {
        ClassificationResult? e = index < expected.Results.Count ? expected.Results[index] : null;
        ClassificationResult? a = index < actual.Results.Count ? actual.Results[index] : null;
        if (e is not null && a is not null && e.Language.Id == a.Language.Id &&
            BitConverter.DoubleToInt64Bits(e.Score) == BitConverter.DoubleToInt64Bits(a.Score))
        {
            continue;
        }

        topTwoDiffers |= index < 2;
        difference ??= $"rank {index}: Ruby {Describe(e)} vs managed {Describe(a)}";
    }

    if (difference is not null && expected.Results.Count != actual.Results.Count)
    {
        difference += $" (counts {expected.Results.Count} vs {actual.Results.Count})";
    }

    return difference;
}

static string Describe(ClassificationResult? result) =>
    result is null ? "<none>" : $"{result.Language.Name} {result.Score.ToString("R", CultureInfo.InvariantCulture)}";

internal sealed record Case(string Source, string Name, byte[] Data, string OptionsName, ClassificationOptions Options);

internal sealed record BenchOptions(string RepositoryRoot, string? SamplesRoot, string SnippetsRoot, string? Corpus, string? Manifest, int[] Trims, int Rounds, int Threads, int Stride, string? Output)
{
    public static BenchOptions Parse(string[] args)
    {
        string root = FindRepositoryRoot();
        string? samples = Path.Combine(root, "extern", "linguist", "samples");
        string snippets = Path.Combine(root, "benchmarks", "ClassifierParity", "snippets");
        string? corpus = null, manifest = null, output = null;
        int[] trims = [256, 2048, 16384, 0];
        int rounds = 3, threads = Environment.ProcessorCount, stride = 1;
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--samples": samples = Next(); break;
                case "--no-samples": samples = null; break;
                case "--snippets": snippets = Next(); break;
                case "--corpus": corpus = Next(); break;
                case "--manifest": manifest = Next(); break;
                case "--trims": trims = [.. Next().Split(',').Select(t => t == "full" ? 0 : int.Parse(t, CultureInfo.InvariantCulture))]; break;
                case "--rounds": rounds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--threads": threads = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--stride": stride = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--output": output = Next(); break;
                default:
                    Console.Error.WriteLine("""
                        Usage: ClassifierParity [--samples <dir> | --no-samples] [--snippets <dir>] [--corpus <prefixes.bin> --manifest <manifest.jsonl>]
                                                [--trims 256,2048,16384,full] [--stride <n>] [--rounds <n>] [--threads <n>] [--output <file.json>]
                        """);
                    Environment.Exit(2);
                    break;
            }
        }

        if ((corpus is null) != (manifest is null))
        {
            throw new ArgumentException("--corpus and --manifest go together.");
        }

        return new BenchOptions(root, samples, snippets, corpus, manifest, trims, rounds, threads, stride, output);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MBW.GHLinguist.slnx")))
            {
                return dir.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}

internal static class Corpus
{
    private static readonly ClassificationOptions Defaults = new();

    private static readonly ClassificationOptions Production = new()
    {
        AllowedTypes = LanguageTypeMask.Programming | LanguageTypeMask.Data | LanguageTypeMask.Markup,
        MaximumBytes = ClassificationOptions.MaximumAllowedBytes,
    };

    public static List<Case> Load(BenchOptions options)
    {
        var cases = new List<Case>();

        // Hand-written short snippets plus generated edge cases: exercise every option set.
        foreach ((string name, byte[] data) in Snippets(options.SnippetsRoot).Concat(EdgeCases()))
        {
            cases.Add(new Case("snippets", name, data, "defaults", Defaults));
            cases.Add(new Case("snippets", name, data, "production", Production));
            cases.Add(new Case("snippets", name, data, "max 64 B", new ClassificationOptions { MaximumBytes = 64 }));
            cases.Add(new Case("snippets", name, data, "programming only", new ClassificationOptions { AllowedTypes = LanguageTypeMask.Programming }));
        }

        // Linguist's own samples (its classifier training set), trimmed to prefixes so most cases are short.
        if (options.SamplesRoot is not null && Directory.Exists(options.SamplesRoot))
        {
            string[] files = Directory.GetFiles(options.SamplesRoot, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            using LinguistRuntime registry = LinguistRuntime.Create();
            for (int index = 0; index < files.Length; index += options.Stride)
            {
                string file = files[index];
                string name = Path.GetRelativePath(options.SamplesRoot, file);
                byte[] content = File.ReadAllBytes(file);
                foreach (int trim in options.Trims)
                {
                    byte[] data = trim > 0 && content.Length > trim ? content[..trim] : content;
                    if (trim > 0 && content.Length <= trim && options.Trims.Contains(0))
                    {
                        continue; // identical to the full-file case
                    }

                    cases.Add(new Case("linguist samples", name, data, trim > 0 ? $"production, first {trim} B" : "production, full", Production));
                }

                // Linguist's test_classify_ambiguous_languages: restrict to the languages the extension maps to.
                IReadOnlyList<LinguistLanguage> byExtension = Path.GetExtension(file).Length > 0 ? registry.FindByExtension(file) : [];
                if (byExtension.Count > 1)
                {
                    cases.Add(new Case("linguist ambiguous ext", name, content, $"candidates {string.Join("/", byExtension.Select(l => l.Name))}",
                        new ClassificationOptions { CandidateLanguageIds = [.. byExtension.Select(l => l.Id)] }));
                }
            }
        }

        // Optional external corpus: concatenated prefixes plus a JSONL manifest of id/offset/length.
        if (options.Corpus is not null && options.Manifest is not null)
        {
            byte[] blob = File.ReadAllBytes(options.Corpus);
            int seen = 0;
            foreach (string line in File.ReadLines(options.Manifest))
            {
                if (seen++ % options.Stride != 0)
                {
                    continue;
                }

                JsonElement entry = JsonDocument.Parse(line).RootElement;
                int offset = entry.GetProperty("offset").GetInt32(), length = entry.GetProperty("length").GetInt32();
                cases.Add(new Case("external corpus", $"#{entry.GetProperty("id").GetInt32()}", blob[offset..(offset + length)], "production", Production));
            }
        }

        return cases;
    }

    private static IEnumerable<(string, byte[])> Snippets(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        string[] files = Directory.GetFiles(root);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (Path.GetFileName(file).StartsWith('.'))
            {
                continue; // .gitattributes
            }

            yield return (Path.GetFileName(file), File.ReadAllBytes(file));
        }
    }

    private static IEnumerable<(string, byte[])> EdgeCases()
    {
        yield return ("<empty>", []);
        yield return ("<space>", " "u8.ToArray());
        yield return ("<nul>", [0]);
        yield return ("<invalid utf-8>", [0xC3, 0x28, 0xA0, 0xA1, 0xFF, (byte)'i', (byte)'n', (byte)'t']);
        yield return ("<shebang only>", "#!/usr/bin/env"u8.ToArray());
        yield return ("<env shebang with args>", "#!/usr/bin/env -S A=1 python3 -u\nprint(1)\n"u8.ToArray());
        yield return ("<unterminated comment>", "/* never closed\nint x;"u8.ToArray());
        yield return ("<unterminated string>", "\"never closed\\"u8.ToArray());
        yield return ("<mixed comment styles>", "<!-- a --> {- b -} (* c *) \"\"\" d \"\"\" ''' e ''' // f\n# g\n-- h\n"u8.ToArray());
        yield return ("<embedded nul>", "int main() { return 0; }\0\0tail"u8.ToArray());
        yield return ("<repeated token>", Encoding.ASCII.GetBytes(string.Join(' ', Enumerable.Repeat("end", 20000))));
        yield return ("<numbers and punctuation>", "1, 2.5e10; 0x1F -> {[()]} && || :: => <= >= != == +="u8.ToArray());
    }
}
