using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.SunnyCalculator;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace LazerSR.Hook;

public static class SunnyCalculatorRunner
{
    public static double Calculate(IBeatmap beatmap, IReadOnlyList<Mod> mods, CancellationToken cancellationToken = default)
    {
        if (beatmap is not ManiaBeatmap)
            return 0;

        return new SunnyManiaDifficultyCalculator().Calculate(beatmap, mods, cancellationToken);
    }

    // Drop-in replacement for BeatmapDifficultyCache.GetBindableDifficulty: returns an IBindable<StarDifficulty>
    // that the caller can assign to StarRatingDisplay.Current. The bindable is updated once the sunny calculation completes.
    // Non-mania beatmaps remain at StarDifficulty(0, 0).
    public static IBindable<StarDifficulty> GetBindableDifficulty(WorkingBeatmap working, RulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken = default)
    {
        var bindable = new Bindable<StarDifficulty>(default);

        if (ruleset.ShortName != "mania")
            return bindable;

        Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IBeatmap playable = working.GetPlayableBeatmap(ruleset, mods, cancellationToken);
                double sunny = Calculate(playable, mods, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                bindable.Value = new StarDifficulty(sunny, 0);
            }
            catch (System.OperationCanceledException) { /* expected */ }
            catch (System.Exception e)
            {
                Trace.WriteLine($"[LazerSR] SunnyCalculatorRunner.GetBindableDifficulty failed: {e}");
            }
        }, cancellationToken);

        return bindable;
    }
}
