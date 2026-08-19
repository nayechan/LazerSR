using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace LazerSR.SunnyCalculator.Tuning;

/// <summary>
/// Sweeps sunny 23 times over one already-converted beatmap: once at the universal point, then +-H
/// (central difference, unit space) on each of <see cref="PersonalBox.Tuned"/>'s 11 constants. After
/// this, a personal fit (<see cref="PersonalFitSolver"/>) never has to call sunny again for that map.
///
/// Every sweep runs through <see cref="SunnyConstants.WithIsolatedDiff{T}"/>, so none of it is visible
/// to any other sunny consumer running concurrently (see <see cref="SunnyConstants"/>'s class doc).
/// </summary>
public static class PersonalJacobianBaker
{
    /// <summary><see cref="Sr0"/> at the universal point, and the unit-space slope for each of <see cref="PersonalBox.Tuned"/>.</summary>
    public record Result(double Sr0, double[] Jacobian);

    /// <param name="beatmap">Already playable (mods applied) - the same beatmap is reused for all 23 sweeps, since only the constants change between them, not the chart.</param>
    public static Result Bake(IBeatmap beatmap, IReadOnlyList<Mod>? mods, CancellationToken token = default)
    {
        var calculator = new SunnyManiaDifficultyCalculator();
        double[] baseDeltas = UniversalDiff.Deltas;

        double sr0 = SunnyConstants.WithIsolatedDiff(baseDeltas, () => calculator.Calculate(beatmap, mods, token));

        var jacobian = new double[PersonalBox.Tuned.Length];

        for (int slot = 0; slot < PersonalBox.Tuned.Length; slot++)
        {
            token.ThrowIfCancellationRequested();

            int index = PersonalBox.Index[slot];
            double step = PersonalBox.RealStep(slot);

            double[] plusDeltas = (double[])baseDeltas.Clone();
            plusDeltas[index] += step;
            double srPlus = SunnyConstants.WithIsolatedDiff(plusDeltas, () => calculator.Calculate(beatmap, mods, token));

            double[] minusDeltas = (double[])baseDeltas.Clone();
            minusDeltas[index] -= step;
            double srMinus = SunnyConstants.WithIsolatedDiff(minusDeltas, () => calculator.Calculate(beatmap, mods, token));

            jacobian[slot] = (srPlus - srMinus) / (2.0 * PersonalBox.H);
        }

        return new Result(sr0, jacobian);
    }
}
