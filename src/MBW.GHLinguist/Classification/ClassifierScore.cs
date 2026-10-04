using System.Globalization;

namespace MBW.GHLinguist.Classification;

/// <summary>The score rule shared by <see cref="LinguistRuntime.Classify" /> and <see cref="LinguistRuntime.ClassifyDotNet" />.</summary>
internal static class ClassifierScore
{
    /// <summary>How far above 1 a score may round before it is treated as invalid rather than clamped.</summary>
    internal const double Tolerance = 1e-12;

    /// <summary>
    /// Linguist's score (<c>Classifier.similarity</c>) is the dot product of the L2-normalized input vector and an
    /// L2-normalized centroid, so it is at most 1 in exact arithmetic. In doubles, the two normalizations and the sum
    /// each round, so an input identical to a language's only training sample can score a few ULPs above 1 (up to
    /// 1.0000000000000009 on Linguist's own samples). Ruby Linguist itself returns these values: it documents the
    /// score as between 0.0 and 1.0 but neither checks nor clamps it, and only ranks by it. They are not a .NET
    /// difference; the managed port reproduces Ruby's arithmetic bit for bit. Those are clamped to 1; a score beyond
    /// <see cref="Tolerance" />, non-finite, or not positive is still rejected as a real fault.
    /// </summary>
    internal static double Normalize(double score)
    {
        if (!double.IsFinite(score) || score <= 0 || score > 1 + Tolerance)
        {
            throw new LinguistException($"The Linguist classifier returned invalid score {score.ToString("R", CultureInfo.InvariantCulture)}.");
        }

        return Math.Min(score, 1);
    }
}
