using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LazerSR.Hook.PersonalSunny;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace LazerSR.Hook.Patches;

/// <summary>
/// Postfix on <see cref="Player.ImportScore"/> - the one place every real solo/multiplayer completion
/// passes through with the finished <see cref="Score"/> already built, before it's written to realm.
/// <para>
/// <c>InfiniteTrainingPlayer</c>/<c>SectionPracticePlayer</c>/<c>ReplayPlayer</c> all override this
/// method without calling <c>base.ImportScore</c> (safety.md's server-isolation table), so this patch -
/// targeting the base <see cref="Player"/> implementation - never fires for local-only training,
/// section practice, or replay/spectate watching. Nothing here needs to filter those out separately.
/// </para>
/// Read-only: only reads the already-built <see cref="Score"/>, never writes to it or to realm.
/// </summary>
[HarmonyPatch]
public static class PersonalSunnyScoreCollectorPatch
{
    private static BeatmapManager? _beatmapManager;
    private static RulesetStore? _rulesetStore;
    private static IAPIProvider? _api;

    public static MethodBase? TargetMethod() => AccessTools.Method(typeof(Player), "ImportScore");

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance, Score score)
    {
        try
        {
            if (__instance is not CompositeDrawable owner) return;

            resolveDependencies(owner);

            if (_beatmapManager != null && _rulesetStore != null)
                PersonalSunnyService.EnsureDependencies(_beatmapManager, _rulesetStore);

            int? localUserId = _api?.LocalUser.Value.Id;
            var scoreInfo = score.ScoreInfo;

            Task.Run(() => PersonalSunnyService.RecordScore(scoreInfo, localUserId));
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyScoreCollectorPatch.Postfix failed: {e}");
        }
    }

    private static void resolveDependencies(CompositeDrawable owner)
    {
        if (_beatmapManager != null && _rulesetStore != null && _api != null) return;

        var deps = AccessTools.Property(typeof(CompositeDrawable), "Dependencies")?.GetValue(owner) as IReadOnlyDependencyContainer;
        if (deps == null) return;

        _beatmapManager ??= deps.Get(typeof(BeatmapManager)) as BeatmapManager;
        _rulesetStore ??= deps.Get(typeof(RulesetStore)) as RulesetStore;
        _api ??= deps.Get(typeof(IAPIProvider)) as IAPIProvider;
    }
}
