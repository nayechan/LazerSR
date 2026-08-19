using System;

namespace LazerSR.SunnyCalculator.Tuning;

/// <summary>
/// The personalised diff (개인화 diff): how sunny's constants should shift for one particular player,
/// on top of <see cref="UniversalDiff"/>.
///
/// Unlike <see cref="UniversalDiff"/> this is mutable at runtime - fitted from a player's own accuracy
/// records (<see cref="PersonalFitSolver"/>) and refit whenever those records change. All zero until a
/// fit has actually run, which reproduces sunny+ v1 exactly (see <see cref="DiffCombiner"/>'s class doc
/// for why this never touches the process-wide default that every other sunny consumer reads).
/// </summary>
public static class PersonalDiff
{
    /// <summary>Deltas from the universal point, indexed by <see cref="SunnyConstants.Names"/>. All zero until <see cref="Update"/> is called.</summary>
    public static double[] Deltas { get; private set; } = new double[SunnyConstants.Count];

    /// <summary>Fires whenever <see cref="Deltas"/> is replaced.</summary>
    public static event Action? Changed;

    /// <summary>Replaces the personal deltas (a full <see cref="SunnyConstants.Count"/>-length vector, e.g. from <see cref="PersonalFitSolver.ToRealDeltas"/>).</summary>
    public static void Update(double[] deltas)
    {
        if (deltas.Length != SunnyConstants.Count)
            throw new ArgumentException($"expected {SunnyConstants.Count} deltas, got {deltas.Length}");

        Deltas = deltas;
        Changed?.Invoke();
    }

    /// <summary>Back to "average player" - every delta zero.</summary>
    public static void Reset() => Update(new double[SunnyConstants.Count]);

    /// <summary>
    /// <see cref="UniversalDiff"/> + <see cref="Deltas"/>, element-wise - the full stock-relative vector
    /// for <see cref="SunnyConstants.WithIsolatedDiff{T}"/> to use when a caller wants this player's
    /// personalised constants rather than the everyone-diff alone.
    /// </summary>
    public static double[] CombinedWithUniversal()
    {
        var combined = new double[SunnyConstants.Count];

        for (int i = 0; i < combined.Length; i++)
            combined[i] = UniversalDiff.Deltas[i] + Deltas[i];

        return combined;
    }
}
