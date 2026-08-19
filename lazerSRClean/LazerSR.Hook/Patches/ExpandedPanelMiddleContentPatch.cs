using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LazerSR.SunnyCalculator;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osuTK;

namespace LazerSR.Hook.Patches;

[HarmonyPatch]
public static class ExpandedPanelMiddleContentPatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Screens.Ranking.Expanded.ExpandedPanelMiddleContent";

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
        return type == null ? null : AccessTools.Method(type, "LoadComplete");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            if (__instance is not Drawable owner) return;

            AccessHelper.TryGet<ScoreInfo>(owner.GetType(), "score", owner, out var score);
            if (score == null)
            {
                HookLog.Write("[LazerSR] ExpandedPanelMiddleContentPatch: score field not found.");
                return;
            }

            InsertSunnyPill_4_2(owner, score);
            InsertClassicAccuracy_4_2(owner, score);
            InsertSunnyPP_4_2(owner, score);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] ExpandedPanelMiddleContentPatch.Postfix failed: {ex}");
        }
    }

    // This panel's score is only the one actually watched/played if its ID matches what
    // ClassicAccuracyWidget last published (see ClassicAccuracyState) - any other score
    // (leaderboard entries, neighbouring panels) never had that value computed, so "-".
    private static void InsertClassicAccuracy_4_2(Drawable owner, ScoreInfo score)
    {
        string text = ClassicAccuracyState.ScoreId == score.ID && ClassicAccuracyState.Accuracy.HasValue
            ? $"{ClassicAccuracyState.Accuracy.Value:F2}%"
            : "-";

        AppendStatisticLine(owner, typeof(AccuracyStatistic), text, "InsertClassicAccuracy_4_2");
    }

    // Same contract as classic accuracy above, backed by SunnyPPState (published live by
    // SunnyPPWidget) instead - a sunnyPP figure only exists for the exact score it was
    // computed for while that replay was actually being watched/played.
    private static void InsertSunnyPP_4_2(Drawable owner, ScoreInfo score)
    {
        string text = SunnyPPState.ScoreId == score.ID && SunnyPPState.Pp.HasValue
            ? $"{SunnyPPState.Pp.Value}pp"
            : "-";

        AppendStatisticLine(owner, typeof(PerformanceStatistic), text, "InsertSunnyPP_4_2");
    }

    // Locates the given StatisticDisplay subtype (AccuracyStatistic / PerformanceStatistic /
    // ...) among owner's children and appends a text line below its existing header+value -
    // both call sites share this exact insertion mechanics, only the source type/text differ.
    private static void AppendStatisticLine(Drawable owner, Type statisticType, string text, string logTag)
    {
        try
        {
            var stat = AccessHelper.FindFirstChildOfType(owner, statisticType);
            if (stat is not CompositeDrawable statComposite)
            {
                HookLog.Write($"[LazerSR] {logTag}: {statisticType.Name} not found.");
                return;
            }

            var internalChildrenProp = AccessTools.Property(typeof(CompositeDrawable), "InternalChildren");
            if (internalChildrenProp?.GetValue(statComposite) is not IEnumerable internalChildren)
            {
                HookLog.Write($"[LazerSR] {logTag}: InternalChildren not found.");
                return;
            }

            FillFlowContainer? flow = null;
            foreach (var child in internalChildren)
            {
                if (child is FillFlowContainer f)
                {
                    flow = f;
                    break;
                }
            }

            if (flow == null)
            {
                HookLog.Write($"[LazerSR] {logTag}: FillFlowContainer not found.");
                return;
            }

            flow.Add(new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Text = text,
                Font = OsuFont.Torus.With(size: 20, fixedWidth: true),
                Spacing = new Vector2(-2, 0),
            });

            HookLog.Write($"[LazerSR] {logTag}: inserted ({text}).");
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] {logTag} failed: {ex}");
        }
    }

    private static void InsertSunnyPill_4_2(Drawable owner, ScoreInfo score)
    {
        try
        {
            var starRating = AccessHelper.FindFirstChildOfType(owner, typeof(StarRatingDisplay));
            if (starRating == null)
            {
                HookLog.Write("[LazerSR] InsertSunnyPill_4_2: StarRatingDisplay not found.");
                return;
            }

            var parentProp = AccessTools.Property(typeof(Drawable), "Parent");
            var parent = parentProp?.GetValue(starRating) as CompositeDrawable;
            if (parent == null)
            {
                HookLog.Write("[LazerSR] InsertSunnyPill_4_2: parent not found.");
                return;
            }

            // Each panel here shows a different score (own beatmap + own mods) - the shared
            // SunnyState.CurrentSr (song select / loading screen only) does not track this,
            // so this location must recompute for the exact score this panel is displaying.
            if (score.Ruleset.OnlineID != 3)
                return; // not mania, sunnySR doesn't apply here

            var depsProp = AccessTools.Property(typeof(CompositeDrawable), "Dependencies");
            var deps = depsProp?.GetValue(owner) as IReadOnlyDependencyContainer;
            var beatmapManager = deps?.Get(typeof(BeatmapManager)) as BeatmapManager;
            if (beatmapManager == null)
            {
                HookLog.Write("[LazerSR] InsertSunnyPill_4_2: BeatmapManager not resolved.");
                return;
            }

            // Local bindable, not SunnyState.CurrentSr - each panel's value is independent
            // and nothing else needs to observe it.
            var localSr = new Bindable<StarDifficulty>();

            var pill = new StarRatingDisplay(default, animated: true)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Alpha = 0f,
            };
            pill.Current = localSr;

            var addInternal = AccessTools.Method(typeof(CompositeDrawable), "AddInternal", new[] { typeof(Drawable) });
            addInternal?.Invoke(parent, new object[] { pill });

            var scheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", new[] { typeof(Action) });

            Task.Run(() =>
            {
                try
                {
                    var working = beatmapManager.GetWorkingBeatmap(score.BeatmapInfo);
                    double sr = SunnyRunner.Calculate(working, score.Ruleset, score.Mods);

                    scheduleMethod?.Invoke(owner, new object[]
                    {
                        (Action)(() =>
                        {
                            localSr.Value = new StarDifficulty(sr, 0);
                            pill.Alpha = 1f;
                        })
                    });
                }
                catch (Exception ex)
                {
                    HookLog.Write($"[LazerSR] InsertSunnyPill_4_2 recalculation failed: {ex.Message}");
                }
            });

            HookLog.Write("[LazerSR] InsertSunnyPill_4_2: pill inserted, recalculating.");
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] InsertSunnyPill_4_2 failed: {ex}");
        }
    }
}
