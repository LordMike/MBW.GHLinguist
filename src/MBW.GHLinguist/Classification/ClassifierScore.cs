using System.Globalization;

namespace MBW.GHLinguist.Classification;

/// <summary>The score rule shared by <see cref="LinguistRuntime.Classify" /> and <see cref="LinguistRuntime.ClassifyDotNet" />.</summary>
internal static class ClassifierScore
{
    /// <summary>How far above 1 a score may round before it is treated as invalid rather than clamped.</summary>
    internal const double Tolerance = 1e-12;

    /// <summary>
    /// Linguist's score is a cosine similarity of unit vectors, so it is at most 1 in exact arithmetic, but the
    /// floating-point sum can land a few ULPs above 1 (for example 1.0000000000000002 for an input identical to a
    /// language's only sample). Those are clamped to 1; a score beyond <see cref="Tolerance" />, non-finite, or not
    /// positive is still rejected.
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
