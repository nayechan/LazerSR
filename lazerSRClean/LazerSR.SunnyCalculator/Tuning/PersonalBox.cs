using System;
using System.Linq;

namespace LazerSR.SunnyCalculator.Tuning;

/// <summary>
/// The 11 constants a per-player fit is allowed to move, and the box that step lives in.
///
/// The box is centred on the universal point (stock + <see cref="UniversalDiff"/>), not on stock —
/// the universal fit ran three of these constants to the edge of its own (stock-centred) box, so a
/// personal step from there could only move one way. Recentring gives every constant room in both
/// directions. Width is deliberately the same fraction of stock as the universal box, so one ridge
/// penalty (in <see cref="PersonalFitSolver"/>) treats all 11 constants alike.
/// </summary>
public static class PersonalBox
{
    /// <summary>Personal box half-width, as a fraction of stock. Same value the universal box used.</summary>
    public const double HalfWidth = 0.30;

    /// <summary>Central-difference step in unit space (u in [0,1], u=0.5 is the universal point).</summary>
    public const double H = 0.075;

    /// <summary>
    /// The 11 tuned constants, in the same order <see cref="PersonalFitSolver"/> and every baked
    /// Jacobian use. Matches <see cref="UniversalDiff"/>'s own list — the everyone-diff moved exactly
    /// these, so they are what a per-player fit has evidence about.
    /// </summary>
    public static readonly string[] Tuned =
    {
        "UnevennessFloor", "MixFirst", "StreamBoostMinRatio", "ChordScale", "StreamBoostMaxRatio",
        "PressingWeight", "LnDensityEarlyMs", "LongNoteBoost", "ReleaseDensityNorm",
        "UnevennessHighThreshold", "FastPressureCoefficient",
    };

    /// <summary>Index into <see cref="SunnyConstants.Stock"/>/<see cref="SunnyConstants.Names"/> for each of <see cref="Tuned"/>.</summary>
    public static readonly int[] Index = Tuned.Select(SunnyConstants.IndexOf).ToArray();

    static PersonalBox()
    {
        if (Index.Any(i => i < 0))
            throw new ArgumentException("PersonalBox.Tuned contains a name SunnyConstants doesn't know.");
    }

    /// <summary>The universal-point value of tuned constant <paramref name="slot"/> (stock + everyone-diff).</summary>
    public static double UniversalValue(int slot) => SunnyConstants.Stock[Index[slot]] + UniversalDiff.Deltas[Index[slot]];

    /// <summary>Real-unit width of the personal box for tuned constant <paramref name="slot"/> (hi - lo).</summary>
    public static double RealWidth(int slot) => 2.0 * HalfWidth * Math.Abs(SunnyConstants.Stock[Index[slot]]);

    /// <summary>Real-unit central-difference step for tuned constant <paramref name="slot"/> (H fraction of the box width).</summary>
    public static double RealStep(int slot) => H * RealWidth(slot);
}
