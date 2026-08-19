using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LazerSR.SunnyCalculator;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;

namespace LazerSR.Hook.Patches;

[HarmonyPatch]
public static class DifficultyIconTooltipPatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Beatmaps.Drawables.DifficultyIconTooltip";

    // DifficultyIconTooltip is a single reused instance per DifficultyIcon - SetContent is
    // called again every time the hovered target changes, so the pill is inserted once but
    // must be recalculated on each call.
    private sealed class PillState
    {
        public readonly Bindable<StarDifficulty> Sr = new();
        public StarRatingDisplay Pill = null!;
        public CancellationTokenSource? Cts;
        public string? Key;
    }

    private static readonly ConditionalWeakTable<object, PillState> _states = new();

    // Game-wide singleton; the tooltip's own Dependencies may not be populated on the very
    // first SetContent, so keep the first successful resolution around.
    private static BeatmapManager? _beatmapManager;

    private static readonly MethodInfo? _scheduleMethod =
        AccessTools.Method(typeof(Drawable), "Schedule", new[] { typeof(Action) });

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
        return type == null ? null : AccessTools.Method(type, "SetContent");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            if (__instance is not Drawable owner) return;

            Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
            if (type == null) return;

            if (!_states.TryGetValue(__instance, out var state))
            {
                state = new PillState();
                if (!InsertSunnyPill(type, owner, state)) return;
                _states.Add(__instance, state);
            }

            Recalculate(type, owner, state);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] DifficultyIconTooltipPatch.Postfix failed: {ex}");
        }
    }

    private static bool InsertSunnyPill(Type type, Drawable owner, PillState state)
    {
        var srDrawable = AccessTools.Field(type, "starRating")?.GetValue(owner) as Drawable;
        if (srDrawable == null)
        {
            HookLog.Write("[LazerSR] DifficultyIconTooltipPatch: starRating not found.");
            return false;
        }

        var parent = AccessTools.Property(typeof(Drawable), "Parent")?.GetValue(srDrawable) as CompositeDrawable;
        if (parent == null)
        {
            HookLog.Write("[LazerSR] DifficultyIconTooltipPatch: parent not found.");
            return false;
        }

        state.Pill = new StarRatingDisplay(default, animated: true)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
        };
        state.Pill.Current = state.Sr;

        // Vertical FillFlowContainer: [0]=difficultyName, [1]=starRating(existing), [2]=insert here
        var insertMethod = AccessTools.Method(parent.GetType(), "Insert", new[] { typeof(int), typeof(Drawable) });
        insertMethod?.Invoke(parent, new object[] { 2, state.Pill });

        return true;
    }

    // The pill used to be bound to the shared SunnyState.CurrentSr, which is only ever written
    // by song select / the loading screen - in a multiplayer lobby that value has nothing to do
    // with the queue item being hovered, so it showed a stale figure. Recalculate for exactly
    // the hovered beatmap + ruleset + mods instead, showing the default (0) while that runs or
    // when the beatmap isn't available locally.
    private static void Recalculate(Type type, Drawable owner, PillState state)
    {
        object? content = AccessTools.Field(type, "displayedContent")?.GetValue(owner);
        if (content == null) return;

        Type contentType = content.GetType();
        var beatmapInfo = AccessTools.Field(contentType, "BeatmapInfo")?.GetValue(content) as IBeatmapInfo;
        var ruleset = AccessTools.Field(contentType, "Ruleset")?.GetValue(content) as IRulesetInfo;
        var mods = AccessTools.Field(contentType, "Mods")?.GetValue(content) as Mod[];

        // osu.Framework's TooltipContainer re-pushes the content every frame while hovered, and
        // DifficultyIcon.TooltipContent hands out a freshly allocated object each time - so this
        // runs per frame with an object that never compares equal to the previous one. Restart
        // the calculation only when the described target actually changed, otherwise each frame
        // would cancel the previous attempt and the value could never settle.
        string key = describe(beatmapInfo, ruleset, mods);
        if (state.Key == key) return;

        state.Key = key;
        state.Cts?.Cancel();
        state.Cts = null;
        state.Sr.Value = default;

        // sunnySR is mania-only - anywhere else the pill would just be a permanent zero.
        if (beatmapInfo == null || ruleset == null || ruleset.OnlineID != 3)
        {
            state.Pill.Alpha = 0f;
            return;
        }

        state.Pill.Alpha = 1f;

        var beatmapManager = resolveBeatmapManager(owner);
        if (beatmapManager == null) return;

        // Mods are shared with the UI that owns them; the calculator applies them to its own
        // conversion, so hand it private copies (see safety.md, "라이브 vs 시뮬레이션 인스턴스").
        IReadOnlyList<Mod> modCopies = mods?.Select(m => m.DeepClone()).ToArray() ?? [];

        var cts = new CancellationTokenSource();
        state.Cts = cts;
        var ct = cts.Token;

        string md5 = beatmapInfo.MD5Hash;
        int onlineId = beatmapInfo.OnlineID;

        Task.Run(() =>
        {
            try
            {
                // Playlist items carry an online beatmap (APIBeatmap), so the local copy has to
                // be looked up. MD5 first - that is what osu! itself treats as "locally available"
                // for a playlist item - falling back to the online id.
                BeatmapInfo? local = beatmapInfo as BeatmapInfo;

                if (local == null && !string.IsNullOrEmpty(md5))
                    local = beatmapManager.QueryBeatmap(b => b.MD5Hash == md5);

                if (local == null && onlineId > 0)
                    local = beatmapManager.QueryBeatmap(b => b.OnlineID == onlineId);

                // Not downloaded - leave the default (0).
                if (local == null || ct.IsCancellationRequested) return;

                var working = beatmapManager.GetWorkingBeatmap(local);
                double sr = SunnyRunner.Calculate(working, ruleset, modCopies, ct);

                if (ct.IsCancellationRequested) return;

                _scheduleMethod?.Invoke(owner, new object[]
                {
                    (Action)(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            state.Sr.Value = new StarDifficulty(sr, 0);
                    })
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                HookLog.Write($"[LazerSR] DifficultyIconTooltipPatch recalculation failed: {ex.Message}");
            }
        }, ct);
    }

    // Identifies what the tooltip is currently describing. Mod settings are included because a
    // rate-adjust value change alone changes the result.
    private static string describe(IBeatmapInfo? beatmapInfo, IRulesetInfo? ruleset, Mod[]? mods)
    {
        if (beatmapInfo == null || ruleset == null) return "none";

        string modString = mods == null
            ? string.Empty
            : string.Join(",", mods.Select(m => $"{m.Acronym}({m.SettingDescription})"));

        return $"{beatmapInfo.MD5Hash}|{beatmapInfo.OnlineID}|{ruleset.OnlineID}|{modString}";
    }

    private static BeatmapManager? resolveBeatmapManager(Drawable owner)
    {
        if (_beatmapManager != null) return _beatmapManager;

        var deps = AccessTools.Property(typeof(CompositeDrawable), "Dependencies")?.GetValue(owner) as IReadOnlyDependencyContainer;
        _beatmapManager = deps?.Get(typeof(BeatmapManager)) as BeatmapManager;

        if (_beatmapManager == null)
            HookLog.Write("[LazerSR] DifficultyIconTooltipPatch: BeatmapManager not resolved.");

        return _beatmapManager;
    }
}
