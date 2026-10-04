using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MBW.GHLinguist.Classification;

namespace MBW.GHLinguist.Tests;

/// <summary>Unit tests for the managed tokenizer and classifier database that need no native assets.</summary>
public sealed partial class ManagedClassifierTests
{
    // Expected tokens come from Linguist's own flex scanner (ext/linguist/lex.linguist_yy.c) for the same bytes.
    [Theory]
    [InlineData("#!/usr/bin/env python\nprint('hi') # done\n", new[] { "SHEBANG#!python", "print", "(", ")", "#", "done" })]
    [InlineData("/* c */ int x = 0x1F; // tail\n", new[] { "COMMENT/*", "int", "x", "=", ";", "//", "tail" })]
    [InlineData("x = \"unterminated\ny", new[] { "x", "=", "y" })]
    [InlineData("a\0b c", new[] { "a", "b", "c" })]
    [InlineData("<!-- x --> <a href=\"\">", new[] { "COMMENT<!--", "<", "a", "href", "=", ">" })]
    [InlineData("#!/bin/sh\n", new[] { "SHEBANG#!sh" })]
    [InlineData("#! /usr/bin/env A=1 node\nlet abcdefghijklmnopqrstuvwxyz = 1;", new[] { "SHEBANG#!node", "let", "abcdefghijklmnop", "=", ";" })]
    [InlineData("", new string[0])]
    public void TokenizerMatchesLinguistsFlexScanner(string source, string[] expected)
    {
        TokenCollector collector = new();
        LinguistTokenizer.Tokenize(Encoding.Latin1.GetBytes(source), ref collector);

        Assert.Equal(expected, collector.Tokens);
    }

    [Fact]
    public void TokenizerIgnoresInputBeyondLinguistsLimit()
    {
        byte[] source = new byte[LinguistTokenizer.MaximumInputBytes + 4];
        source.AsSpan().Fill((byte)' ');
        "tail"u8.CopyTo(source.AsSpan(LinguistTokenizer.MaximumInputBytes));
        TokenCollector collector = new();

        LinguistTokenizer.Tokenize(source, ref collector);

        Assert.Empty(collector.Tokens);
    }

    [Fact]
    public void TokenizerTablesMatchTheLinguistSubmodule()
    {
        string? scanner = FindRepositoryFile("extern", "linguist", "ext", "linguist", "lex.linguist_yy.c");
        Assert.SkipWhen(scanner is null, "The extern/linguist submodule is not checked out.");
        string source = File.ReadAllText(scanner!).ReplaceLineEndings("\n");

        Assert.Equal(Table(source, "yy_accept"), ToInts(LinguistTokenizerTables.Accept));
        Assert.Equal(Table(source, "yy_ec"), ToInts(LinguistTokenizerTables.EquivalenceClasses));
        Assert.Equal(Table(source, "yy_meta"), ToInts(LinguistTokenizerTables.Meta));
        Assert.Equal(Table(source, "yy_base"), ToInts(LinguistTokenizerTables.Base));
        Assert.Equal(Table(source, "yy_def"), ToInts(LinguistTokenizerTables.Default));
        Assert.Equal(Table(source, "yy_nxt"), ToInts(LinguistTokenizerTables.Next));
        Assert.Equal(Table(source, "yy_chk"), ToInts(LinguistTokenizerTables.Check));
        string rules = Path.Combine(Path.GetDirectoryName(scanner!)!, "tokenizer.l");
        Assert.Equal(
            LinguistTokenizerTables.RulesSha256,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(rules).ReplaceLineEndings("\n")))));
        Assert.Contains($"#define YY_END_OF_BUFFER {LinguistTokenizerTables.EndOfBufferAction}\n", source, StringComparison.Ordinal);
        Assert.Contains($"while ( yy_current_state != {LinguistTokenizerTables.JamState} );", source, StringComparison.Ordinal);
        Assert.Contains($"if ( yy_current_state >= {LinguistTokenizerTables.MetaThreshold} )", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseReadsTheGeneratedFile()
    {
        ClassifierDatabase database = ClassifierDatabase.Parse(SamplesTsv(
            [("#{", 0), ("\"", 1), ("\\", 2), ("x", 3)],
            [1.5, 2.0, 1.0e-05, 3.25],
            [("A", [(0, 0.5), (3, 0.25)]), ("B", [(3, 0.125)])]));

        Assert.Equal(0, database.Vocabulary.Find("#{"u8));
        Assert.Equal(1, database.Vocabulary.Find("\""u8));
        Assert.Equal(2, database.Vocabulary.Find("\\"u8));
        Assert.Equal(3, database.Vocabulary.Find("x"u8));
        Assert.Equal(-1, database.Vocabulary.Find("y"u8));
        Assert.Equal([1.5, 2.0, 1.0e-05, 3.25], database.InverseClassFrequencies);
        Assert.Equal(["A", "B"], database.CentroidNames);

        // Term 3 is in both centroids, in centroid order.
        Assert.Equal([0, 1, 1, 1, 3], database.PostingStarts);
        Assert.Equal([0, 0, 1], database.PostingCentroids);
        Assert.Equal([0.5, 0.25, 0.125], database.PostingValues);
    }

    [Fact]
    public void ScoresFollowLinguistsArithmetic()
    {
        ClassifierDatabase database = ClassifierDatabase.Parse(SamplesTsv(
            [("a", 0), ("b", 1)],
            [1.5, 2.5],
            [("A", [(0, 0.6), (1, 0.8)]), ("B", [(1, 1.0)]), ("C", [])]));
        ContentClassifier classifier = new(database);
        double[] scores = new double[3];

        Assert.True(classifier.Score("b a b c"u8, scores));

        // Linguist: vec = {1 => (1 + log 2) * 2.5, 0 => (1 + log 1) * 1.5}, L2-normalized, then dot products.
        double b = (1.0 + Math.Log(2)) * 2.5;
        double a = (1.0 + Math.Log(1)) * 1.5;
        double norm = Math.Sqrt(0.0 + (b * b) + (a * a));
        Assert.Equal(0.0 + (b / norm * 0.8) + (a / norm * 0.6), scores[0]);
        Assert.Equal(0.0 + (b / norm * 1.0), scores[1]);
        Assert.Equal(0.0, scores[2]);
        Assert.False(classifier.Score("c"u8, scores));
    }

    // Linguist's own samples score up to 4 ULPs above 1 against their language's centroid.
    [Theory]
    [InlineData(1.0000000000000002)]
    [InlineData(1.0000000000000004)]
    [InlineData(1.0000000000000007)]
    [InlineData(1.0000000000000009)]
    [InlineData(1 + ClassifierScore.Tolerance)]
    public void ScoresRoundedJustAboveOneAreClampedToOne(double score) => Assert.Equal(1.0, ClassifierScore.Normalize(score));

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(double.Epsilon)]
    public void ScoresWithinRangeAreKept(double score) => Assert.Equal(score, ClassifierScore.Normalize(score));

    [Theory]
    [InlineData(1.000000000002)]
    [InlineData(2.0)]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidScoresAreRejected(double score) => Assert.Throws<LinguistException>(() => ClassifierScore.Normalize(score));

    [Fact]
    public void VocabularyNeverMatchesNonAsciiTokens()
    {
        // Linguist's tokens are binary Ruby strings; Hash#key? only matches them against UTF-8 keys when ASCII-only.
        ClassifierDatabase database = ClassifierDatabase.Parse(SamplesTsv([("é", 0)], [1.0], []));

        Assert.Equal(-1, database.Vocabulary.Find("é"u8));
    }

    [Fact]
    public void DatabaseRejectsMalformedLines()
    {
        Assert.Throws<FormatException>(() => ClassifierDatabase.Parse("vocabulary\ta\t0"u8));
        Assert.Throws<FormatException>(() => ClassifierDatabase.Parse("vocabulary\ta\t0\textra\nicf\t1.0\n"u8));
        Assert.Throws<FormatException>(() => ClassifierDatabase.Parse("vocabulary\ta\t0\nicf\tx\n"u8));
        Assert.Throws<FormatException>(() => ClassifierDatabase.Parse("unknown\n"u8));
    }

    /// <summary>Writes samples.tsv the way eng/linguist/generate-samples.rb does.</summary>
    private static byte[] SamplesTsv(
        (string Term, int Index)[] vocabulary,
        double[] icf,
        (string Name, (int Term, double Value)[] Entries)[] centroids)
    {
        StringBuilder text = new("extnames\tC\t.c\ninterpreters\tC\tcc\n");
        foreach ((string term, int index) in vocabulary)
        {
            text.Append($"vocabulary\t{term}\t{index}\n");
        }

        text.Append("icf").AppendJoin("", icf.Select(value => "\t" + value.ToString("R", CultureInfo.InvariantCulture))).Append('\n');
        foreach ((string name, (int Term, double Value)[] entries) in centroids)
        {
            text.Append($"centroid\t{name}");
            foreach ((int term, double value) in entries)
            {
                text.Append(CultureInfo.InvariantCulture, $"\t{term}\t{value:R}");
            }

            text.Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.Append("sha256\tabc\n").ToString());
    }

    private static int[] ToInts(ReadOnlySpan<short> values)
    {
        int[] result = new int[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            result[index] = values[index];
        }

        return result;
    }

    private static int[] ToInts(ReadOnlySpan<byte> values)
    {
        int[] result = new int[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            result[index] = values[index];
        }

        return result;
    }

    private static int[] Table(string source, string name)
    {
        Match match = Regex.Match(source, $@"static const \w+ {name}\[\d+\] =\s*\{{(.*?)\}}\s*;", RegexOptions.Singleline);
        Assert.True(match.Success, $"{name} is missing from the flex scanner.");
        return [.. NumberPattern().Matches(match.Groups[1].Value).Select(number => int.Parse(number.Value, System.Globalization.CultureInfo.InvariantCulture))];
    }

    internal static string? FindRepositoryFile(params string[] parts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MBW.GHLinguist.slnx")))
            {
                string path = Path.Combine([directory.FullName, .. parts]);
                return File.Exists(path) || Directory.Exists(path) ? path : null;
            }
        }

        return null;
    }

    [GeneratedRegex("-?\\d+")]
    private static partial Regex NumberPattern();

    private struct TokenCollector() : ILinguistTokenSink
    {
        internal List<string> Tokens { get; } = [];

        public readonly void Add(scoped ReadOnlySpan<byte> token) => Tokens.Add(Encoding.Latin1.GetString(token));
    }
}
