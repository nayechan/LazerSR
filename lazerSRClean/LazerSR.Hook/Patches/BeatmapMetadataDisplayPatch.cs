using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LazerSR.SunnyCalculator;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Rulesets.Mods;

namespace LazerSR.Hook.Patches;

[HarmonyPatch]
public static class BeatmapMetadataDisplayPatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Screens.Play.BeatmapMetadataDisplay";

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
            if (__instance is Drawable owner)
            {
                RecalculateForActualBeatmap(owner);
                InsertSunnyPill_4_1(owner);
            }
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] BeatmapMetadataDisplayPatch.Postfix failed: {ex}");
        }
    }

    // SunnyState.CurrentSr is otherwise only written by song-select's DifficultyDisplayPatch,
    // keyed off the local carousel's currently-previewed beatmap. In multiplayer that preview
    // can diverge from the beatmap actually queued/loaded here (host picks the map, client is
    // still free to browse locally), so CurrentSr can be stale by the time this loading screen
    // shows. Recalculate against the beatmap this screen is actually displaying.
    private static void RecalculateForActualBeatmap(Drawable owner)
    {
        try
        {
            Type type = owner.GetType();

            if (!AccessHelper.TryGet<IWorkingBeatmap>(type, "beatmap", owner, out var beatmap) || beatmap == null)
                return;

            if (!AccessHelper.TryGet<IBindable<IReadOnlyList<Mod>>>(type, "Mods", owner, out var modsBindable) || modsBindable == null)
                return;

            var ruleset = beatmap.BeatmapInfo.Ruleset;
            if (ruleset.OnlineID != 3) return;

            IReadOnlyList<Mod> mods = modsBindable.Value;
            var scheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", new[] { typeof(Action) });

            Task.Run(() =>
            {
                try
                {
                    double sr = SunnyRunner.Calculate(beatmap, ruleset, mods);
                    scheduleMethod?.Invoke(owner, new object[] { (Action)(() => SunnyState.CurrentSr.Value = new StarDifficulty(sr, 0)) });
                }
                catch (Exception ex)
                {
                    HookLog.Write($"[LazerSR] BeatmapMetadataDisplayPatch recalculation failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] RecalculateForActualBeatmap failed: {ex}");
        }
    }

    private static void InsertSunnyPill_4_1(Drawable owner)
    {
        try
        {
            Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
            if (type == null) return;

            var versionFlowField = AccessTools.Field(type, "versionFlow");
            var versionFlow = versionFlowField?.GetValue(owner) as CompositeDrawable;
            if (versionFlow == null)
            {
                HookLog.Write("[LazerSR] InsertSunnyPill_4_1: versionFlow not found.");
                return;
            }

            var pill = new StarRatingDisplay(default, animated: true)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
            };
            pill.Current = SunnyState.CurrentSr;

            var addInternal = AccessTools.Method(typeof(CompositeDrawable), "AddInternal", new[] { typeof(Drawable) });
            addInternal?.Invoke(versionFlow, new object[] { pill });

            HookLog.Write("[LazerSR] InsertSunnyPill_4_1: pill inserted.");
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] InsertSunnyPill_4_1 failed: {ex}");
        }
    }
}
