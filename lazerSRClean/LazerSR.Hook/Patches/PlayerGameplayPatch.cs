using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LazerSR.Hook.Calculators;
using LazerSR.Hook.Data;
using LazerSR.Hook.Ipc;
using LazerSR.Hook.Screens;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Screens.Play;

namespace LazerSR.Hook.Patches;

[HarmonyPatch]
public static class PlayerGameplayPatch
{
    private static CancellationTokenSource? _cts;

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName("osu.Game.Screens.Play.Player");
        return type == null ? null : AccessTools.Method(type, "LoadComplete");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            // 무한 트레이닝은 노트가 런타임에 계속 바뀌므로 리플레이 비교가 성립하지 않는다.
            if (__instance is InfiniteTrainingPlayer)
                return;

            var cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _cts, cts)?.Cancel();
            ReplayCompareState.Timeline   = null;
            ReplayCompareState.ReplayDate = string.Empty;
            SectionTimerState.ActiveSections = null;

            var depsProperty = AccessTools.Property(typeof(CompositeDrawable), "Dependencies");
            var deps = depsProperty?.GetValue(__instance) as IReadOnlyDependencyContainer;
            if (deps == null) return;

            var gameplayState = deps.Get(typeof(GameplayState)) as GameplayState;
            var gameplayClock = deps.Get(typeof(IGameplayClock)) as IGameplayClock;
            var realmAccess   = deps.Get(typeof(RealmAccess)) as RealmAccess;
            var apiProvider   = deps.Get(typeof(IAPIProvider)) as IAPIProvider;
            var storage       = deps.Get(typeof(Storage)) as Storage;

            if (gameplayState == null || gameplayClock == null || realmAccess == null || storage == null)
                return;

            if (gameplayState.Beatmap is not ManiaBeatmap)
                return;

            // Snapshot sections pre-computed during song select (finalize for this run)
            SectionTimerState.ActiveSections = SectionTimerState.PendingSections;

            var beatmap        = gameplayState.Beatmap;
            var mods           = gameplayState.Mods;
            string beatmapHash = beatmap.BeatmapInfo.Hash;
            var currentApiMods = mods.Select(m => new APIMod(m)).ToArray();
            string? username   = apiProvider?.LocalUser.Value.Username;

            var token = cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    string? osrPath = BestScoreFinder.FindBestReplayPath(
                        realmAccess, storage, beatmapHash, currentApiMods, username);

                    if (string.IsNullOrEmpty(osrPath) || !File.Exists(osrPath))
                    {
                        HookLog.Write("[LazerSR] PlayerGameplayPatch: no replay found.");
                        return;
                    }

                    token.ThrowIfCancellationRequested();

                    var timeline = ReplayScoreTimeline.Calculate(beatmap, osrPath, out string replayDate);
                    ReplayCompareState.Timeline   = timeline;
                    ReplayCompareState.ReplayDate = replayDate;
                    HookLog.Write($"[LazerSR] PlayerGameplayPatch: timeline ready ({timeline.Length} events), date={replayDate}.");

                    int lastScore = -1;
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(100, token);

                        var tl = ReplayCompareState.Timeline;
                        if (tl == null || tl.Length == 0) continue;

                        double curTime = gameplayClock.CurrentTime;
                        int idx = TimelineSearch(tl, curTime);
                        if (idx < 0) continue;

                        int score = tl[idx].Score;
                        if (score == lastScore) continue;
                        lastScore = score;

                        await PipeServer.BroadcastAsync($"replaycompare:{score}:{tl[idx].Combo}");
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    HookLog.Write($"[LazerSR] PlayerGameplayPatch failed: {ex.Message}");
                }
            }, token);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] PlayerGameplayPatch.Postfix failed: {ex}");
        }
    }

    private static int TimelineSearch(ReplayFrame[] tl, double t)
    {
        if (t < tl[0].Time) return -1;
        int lo = 0, hi = tl.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (tl[mid].Time <= t) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }
}
