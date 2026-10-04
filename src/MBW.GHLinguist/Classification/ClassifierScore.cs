using System.Globalization;

namespace MBW.GHLinguist.Classification;

/// <summary>
/// The score rule shared by <see cref="LinguistRuntime.Classify" /> (Ruby, via the native backend) and
/// <see cref="LinguistRuntime.ClassifyDotNet" /> (the managed port): every score either method returns passes through
/// <see cref="Normalize" />, so both report identical values.
/// </summary>
internal static class ClassifierScore
{
    /// <summary>How far above 1 a score may round before it is treated as invalid rather than clamped.</summary>
    /// <remarks>
    /// The largest overshoot seen on Linguist's own samples is 4 ULPs (about 9e-16). 1e-12 leaves room for longer
    /// sums or other platforms' rounding while staying far below any real fault, such as a score of 2 from a corrupt
    /// classifier database or a mis-normalized vector.
    /// </remarks>
    internal const double Tolerance = 1e-12;

    /// <summary>Clamps a score that floating-point rounding put just above 1, and rejects any other invalid score.</summary>
    /// <remarks>
    /// <para>
    /// Why scores exceed 1: Linguist's score (<c>Classifier.similarity</c> in <c>lib/linguist/classifier.rb</c>) is
    /// the dot product of the L2-normalized input vector and an L2-normalized language centroid, a cosine similarity,
    /// so it is at most 1 in exact arithmetic. In doubles, both normalizations and the sum round. When the input is
    /// (nearly) identical to a language's only training sample, the true result is exactly 1 and the computed one can
    /// land a few ULPs above it: up to 1.0000000000000009 (4 ULPs) on Linguist's own samples, about 2% of which do this.
    /// </para>
    /// <para>
    /// Ruby Linguist itself produces these values; this is not a .NET or CLR difference, and the managed port
    /// reproduces Ruby's arithmetic bit for bit. Linguist documents the score as "between 0.0 and 1.0" but never checks
    /// or clamps it, because it only ranks by it. Version 0.3.0 of this package rejected any score above 1 and so threw
    /// on such inputs.
    /// </para>
    /// <para>
    /// Callers clamp after ranking, so the order is still Linguist's. A score beyond <see cref="Tolerance" />,
    /// non-finite, or not positive is still a real fault and throws.
    /// </para>
    /// </remarks>
    internal static double Normalize(double score)
    {
        if (!double.IsFinite(score) || score <= 0 || score > 1 + Tolerance)
        {
            throw new LinguistException($"The Linguist classifier returned invalid score {score.ToString("R", CultureInfo.InvariantCulture)}.");
        }

        return Math.Min(score, 1);
    }
}
