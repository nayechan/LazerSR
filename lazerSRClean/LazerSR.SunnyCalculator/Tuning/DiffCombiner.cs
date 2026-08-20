namespace LazerSR.SunnyCalculator.Tuning;

/// <summary>
/// The delta vector for <see cref="SunnyConstants"/>'s process-wide default — the one every existing
/// sunny consumer (song select pills, tooltips, results screen, ...) reads.
///
/// This is the everyone-diff alone, on purpose: <see cref="PersonalDiff"/> is deliberately kept out of
/// the process-wide default and only ever applied through <see cref="SunnyConstants.WithIsolatedDiff{T}"/>,
/// scoped to whichever call context needs a personalised value (baking, and later personal-diff
/// consumers). That keeps the two isolated instead of merged here.
/// </summary>
public static class DiffCombiner
{
    /// <summary>
    /// Temporary kill switch for sunny+ (the everyone-diff). Set by the Launcher's sunny+ checkbox via
    /// an environment variable read at hook startup; true (stock behaviour) unless explicitly disabled.
    /// </summary>
    public static bool UniversalDiffEnabled { get; set; } = true;

    public static double[] Combine() => UniversalDiffEnabled ? UniversalDiff.Deltas : new double[SunnyConstants.Count];
}
