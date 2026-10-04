using System.Text;

namespace MBW.GHLinguist.Tests;

/// <summary>
/// <see cref="LinguistRuntime.ClassifyDotNet" /> runs a managed port of Linguist's classifier. These tests run the
/// same inputs through <see cref="LinguistRuntime.Classify" />, Linguist's Ruby classifier, and require identical
/// rankings and bit-identical scores, so a Linguist upgrade or a platform math difference fails here instead of drifting.
/// </summary>
public sealed class ManagedClassifierParityTests
{
    public static bool NativeIntegrationEnabled => NativeLookupParityTests.NativeIntegrationEnabled;

    private static readonly string[] EdgeCases =
    [
        "",
        " ",
        "\0",
        "#!/usr/bin/env python\nprint('hi')\n",
        "#!/usr/bin/env\tA=1\tperl -w\nprint 1;\n",
        "#!/usr/bin/env",
        "/* unterminated comment",
        "\"unterminated string",
        "<!-- x --> {- y -} (* z *) \"\"\" w \"\"\" ''' v ''' .ig\nroff\n..\n/- lean -/",
        "a\0b \"c\0d\" e",
        "é ü 中文 ÿþ",
        "SELECT * FROM t WHERE a <= 1 AND b != 2; -- comment\n",
        "<?php echo $x; ?>\n<div class=\"a\">&nbsp;</div>",
        "\"a\\",
        "\"\0",
        "\"a\\\0",
        "#!",
        "#!/usr/bin/env A=\0 ruby\nputs 1",
        ".ig\nunterminated roff",
        "int main() { return 0; }\0",
    ];

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void ClassifyMatchesLinguistsRubyClassifier()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        ClassificationOptions production = new()
        {
            AllowedTypes = LanguageTypeMask.Programming | LanguageTypeMask.Data | LanguageTypeMask.Markup,
            MaximumBytes = ClassificationOptions.MaximumAllowedBytes,
        };
        ClassificationOptions small = new() { MaximumBytes = 300 };

        int compared = 0;
        foreach (byte[] sample in Samples())
        {
            AssertSameClassification(runtime, sample, new ClassificationOptions());
            AssertSameClassification(runtime, sample, production);
            AssertSameClassification(runtime, sample, small);
            compared++;
        }

        Assert.True(compared > EdgeCases.Length, "No Linguist sample files were compared.");
    }

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void CandidateOrderMatchesLinguistsRubyClassifier()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        Random random = new(1);
        ulong[] candidates = [.. runtime.Languages.Select(language => language.Id)];
        random.Shuffle(candidates);
        ClassificationOptions shuffled = new() { CandidateLanguageIds = candidates };
        ClassificationOptions few = new() { CandidateLanguageIds = candidates[..25], AllowedTypes = LanguageTypeMask.Programming };

        foreach (byte[] sample in Samples().Take(200))
        {
            AssertSameClassification(runtime, sample, shuffled);
            AssertSameClassification(runtime, sample, few);
        }
    }

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public async Task ConcurrentClassificationsAgree()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        byte[][] samples = [.. Samples().Take(100)];
        ClassificationResults[] expected = [.. samples.Select(sample => runtime.ClassifyDotNet(sample))];

        await Task.WhenAll(Enumerable.Range(0, Environment.ProcessorCount).Select(worker => Task.Run(() =>
        {
            for (int index = 0; index < samples.Length; index++)
            {
                int sample = (index + worker) % samples.Length;
                AssertSame(expected[sample], runtime.ClassifyDotNet(samples[sample]));
            }
        })));
    }

    private static void AssertSameClassification(LinguistRuntime runtime, byte[] sample, ClassificationOptions options) =>
        AssertSame(runtime.Classify(sample, options), runtime.ClassifyDotNet(sample, options));

    private static void AssertSame(ClassificationResults expected, ClassificationResults actual)
    {
        Assert.Equal(expected.ConsideredBytes, actual.ConsideredBytes);
        Assert.Equal(expected.Results.Count, actual.Results.Count);
        for (int index = 0; index < expected.Results.Count; index++)
        {
            Assert.Equal(expected.Results[index].Language.Id, actual.Results[index].Language.Id);
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(expected.Results[index].Score),
                BitConverter.DoubleToInt64Bits(actual.Results[index].Score));
        }
    }

    /// <summary>Edge cases, then every fourth file of Linguist's own samples when the submodule is checked out.</summary>
    private static IEnumerable<byte[]> Samples()
    {
        foreach (string edgeCase in EdgeCases)
        {
            yield return Encoding.UTF8.GetBytes(edgeCase);
        }

        string? root = ManagedClassifierTests.FindRepositoryFile("extern", "linguist", "samples");
        if (root is null)
        {
            yield break;
        }

        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        for (int index = 0; index < files.Length; index += 4)
        {
            yield return File.ReadAllBytes(files[index]);
        }
    }
}
