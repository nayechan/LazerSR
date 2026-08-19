namespace LazerSR.SunnyCalculator.Tuning;

/// <summary>
/// The everyone-diff (만인 diff): how sunny's constants should shift for players in general.
///
/// These are sunny+ v1, fitted against 4,273 (map, rate) items from the top-10,000 mania dump by
/// Gauss-Newton on the ranking residual. The absolute star number is never fitted, only the shape of
/// the ranking, so a global offset and scale are profiled out at every step. Eleven of the thirty-nine
/// constants moved; the rest are still stock and their delta is exactly zero.
///
/// Fit quality against player ability: rank correlation 0.9402 -> 0.9704, residual 0.3180 -> 0.2458,
/// maps off by more than half a star 472 -> 189. Source: OsuScoreModel/dev/sunnyplus/sunnyplus_v1.json.
/// </summary>
public static class UniversalDiff
{
    /// <summary>Deltas from stock, indexed by <see cref="SunnyConstants.Names"/>.</summary>
    public static readonly double[] Deltas = build();

    private static double[] build()
    {
        var deltas = new double[SunnyConstants.Count];

        // sunny+ v1 (2026-08-19). Comments give the resulting value and its factor over stock.
        set(deltas, "UnevennessFloor", 0.011328822531794902);          // 0.76133   x1.015
        set(deltas, "MixFirst", 0.12);                                 // 0.52      x1.300
        set(deltas, "StreamBoostMinRatio", -37.489330724420242);       // 122.511   x0.766
        set(deltas, "ChordScale", -232.68988489250478);                // 767.310   x0.767
        set(deltas, "StreamBoostMaxRatio", 108.0);                     // 468.0     x1.300
        set(deltas, "PressingWeight", -0.046951549098395051);          // 0.75305   x0.941
        set(deltas, "LnDensityEarlyMs", 1.4326266658096003);           // 61.433    x1.024
        set(deltas, "LongNoteBoost", 6.0);                             // 12.0      x2.000
        set(deltas, "ReleaseDensityNorm", 15.558470644463242);         // 50.558    x1.445
        set(deltas, "UnevennessHighThreshold", 0.041965362890731064);  // 0.11197   x1.600
        set(deltas, "FastPressureCoefficient", 0.043924507683257452);  // 0.44392   x1.110

        return deltas;
    }

    private static void set(double[] deltas, string name, double delta)
    {
        int index = SunnyConstants.IndexOf(name);
        if (index < 0) throw new System.ArgumentException($"unknown sunny constant '{name}'");

        deltas[index] = delta;
    }
}
