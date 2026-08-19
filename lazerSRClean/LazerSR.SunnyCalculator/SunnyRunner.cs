using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;

namespace LazerSR.SunnyCalculator;

/// <summary>
/// Thin facade over the sunny ManiaDifficultyCalculator.
/// Mirrors osu!'s BeatmapDifficultyCache.computeDifficulty pattern:
///   ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(mods, token)
/// </summary>
public static class SunnyRunner
{
    public static double Calculate(
        IWorkingBeatmap workingBeatmap,
        IRulesetInfo ruleset,
        IReadOnlyList<Mod> mods,
        CancellationToken cancellationToken = default)
    {
        var calc = new osu.Game.Rulesets.Mania.Difficulty.ManiaDifficultyCalculator(ruleset, workingBeatmap);
        var attrs = calc.Calculate(mods, cancellationToken);
        return attrs.StarRating;
    }

    public static List<TimedDifficultyAttributes> CalculateTimed(
        IWorkingBeatmap workingBeatmap,
        IRulesetInfo ruleset,
        IReadOnlyList<Mod> mods,
        CancellationToken cancellationToken = default)
    {
        var calc = new osu.Game.Rulesets.Mania.Difficulty.ManiaDifficultyCalculator(ruleset, workingBeatmap);
        return calc.CalculateTimed(mods, cancellationToken);
    }
}
