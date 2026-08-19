using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LazerSR.SunnyCalculator;
using LazerSR.SunnyCalculator.Tuning;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Scoring;

namespace LazerSR.Hook.PersonalSunny;

/// <summary>
/// Orchestrates the personal-diff pipeline: queue -> bake missing Jacobians -> ridge-solve -> publish
/// to <see cref="PersonalDiff"/> and persist. Both the automatic path (a real score just imported,
/// <see cref="RecordScore"/>) and the manual one (the widget's collect button,
/// <see cref="CollectFromRealmAsync"/>) funnel into the same <see cref="runPipeline"/>.
/// <para>
/// Everything here runs on a background thread the caller provides (<c>Task.Run</c>) - nothing here
/// touches a Drawable. The widget polls the plain state properties below from its own <c>Update()</c>,
/// the same pattern <c>ManiaSimulationProgressDisplay</c> uses, rather than marshalling events back to
/// the update thread.
/// </para>
/// </summary>
public static class PersonalSunnyService
{
    // ---- state the widget polls ----

    public static bool IsBaking { get; private set; }
    public static int BakeTotal { get; private set; }
    public static int BakeDone { get; private set; }
    public static int QueueCount => PersonalSunnyQueueStore.Entries.Count;
    public static double Alpha { get; private set; }
    public static double Beta { get; private set; }
    public static int FitRecordCount { get; private set; }

    /// <summary>Bumped whenever any of the state above changes - the widget compares this each frame instead of subscribing to an event.</summary>
    public static int Version { get; private set; }

    private static readonly object pipeline_lock = new();

    private static BeatmapManager? beatmapManager;
    private static RulesetInfo? maniaRuleset;

    static PersonalSunnyService()
    {
        var fit = PersonalSunnyFitStore.Current;

        if (fit != null)
        {
            PersonalDiff.Update(PersonalFitSolver.ToRealDeltas(fit.UnitStep));
            Alpha = fit.Alpha;
            Beta = fit.Beta;
            FitRecordCount = fit.RecordCount;
        }
    }

    /// <summary>Call from anywhere dependencies are resolvable (widget BDL, or a patch's reflection lookup) - first caller wins, later callers are no-ops.</summary>
    public static void EnsureDependencies(BeatmapManager manager, RulesetStore rulesets)
    {
        beatmapManager ??= manager;
        maniaRuleset ??= rulesets.GetRuleset("mania") as RulesetInfo;
    }

    /// <summary>
    /// Filters and queues one just-completed score, then runs the pipeline for it. Call on a background
    /// thread. Silently does nothing if <paramref name="scoreInfo"/> doesn't qualify (wrong ruleset, not
    /// 4K, didn't pass, not the local player, or a mod outside <see cref="PersonalSunnyModWhitelist"/>).
    /// </summary>
    public static void RecordScore(ScoreInfo scoreInfo, int? localUserOnlineId)
    {
        try
        {
            if (!qualifies(scoreInfo, localUserOnlineId))
                return;

            var (rate, chartMod) = PersonalSunnyModWhitelist.Describe(scoreInfo.Mods);

            var entry = new PersonalSunnyQueueEntry(
                scoreInfo.BeatmapInfo!.MD5Hash, rate, chartMod, scoreInfo.Accuracy, scoreInfo.Date);

            PersonalSunnyQueueStore.Add(entry);
            runPipeline();
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyService.RecordScore failed: {e}");
        }
    }

    private static bool qualifies(ScoreInfo scoreInfo, int? localUserOnlineId)
    {
        if (!scoreInfo.Passed) return false;
        if (scoreInfo.Ruleset.OnlineID != 3) return false;
        if (scoreInfo.BeatmapInfo == null) return false;
        // ManiaBeatmapConverter rounds CircleSize to get the column count - match that, not exact equality.
        if (Math.Round(scoreInfo.BeatmapInfo.Difficulty.CircleSize) != 4) return false;
        if (localUserOnlineId is > 0 && scoreInfo.RealmUser.OnlineID != localUserOnlineId) return false;
        if (!PersonalSunnyModWhitelist.IsAllowed(scoreInfo.Mods)) return false;

        return true;
    }

    /// <summary>
    /// Bulk backfill from local realm history - up to <see cref="PersonalSunnyQueueStore.MaxEntries"/>
    /// of the player's own most recent qualifying mania 4K passes, replacing the current queue. Call on
    /// a background thread (this itself is synchronous).
    /// </summary>
    public static void CollectFromRealmAsync(RealmAccess realm, IAPIProvider api)
    {
        try
        {
            int localUserId = api.LocalUser.Value.Id;

            var collected = realm.Run(r =>
            {
                // ScoreInfo.Passed is [Ignored] (not a persisted realm column) - it can't appear in a
                // realm-side LINQ predicate. qualifies() below already re-checks it per candidate.
                var candidates = r.All<ScoreInfo>()
                                  .Where(s => !s.DeletePending)
                                  .OrderByDescending(s => s.Date)
                                  .ToList();

                var result = new List<PersonalSunnyQueueEntry>();

                foreach (var score in candidates)
                {
                    if (result.Count >= PersonalSunnyQueueStore.MaxEntries)
                        break;

                    if (!qualifies(score, localUserId))
                        continue;

                    var (rate, chartMod) = PersonalSunnyModWhitelist.Describe(score.Mods);
                    result.Add(new PersonalSunnyQueueEntry(score.BeatmapInfo!.MD5Hash, rate, chartMod, score.Accuracy, score.Date));
                }

                // Oldest first, to match the FIFO order Add() would have produced.
                result.Reverse();
                return result;
            });

            ReplaceQueueAndRun(collected);
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyService.CollectFromRealmAsync failed: {e}");
        }
    }

    /// <summary>
    /// Replaces the queue with <paramref name="entries"/> and runs bake+refit. Shared tail for every
    /// queue-population path - <see cref="CollectFromRealmAsync"/> is the only caller today, but this
    /// stays a separate entry point so an alternate source (e.g. a dev fixture) can reuse the same
    /// bake/refit pipeline without duplicating it. Call on a background thread.
    /// </summary>
    public static void ReplaceQueueAndRun(IReadOnlyList<PersonalSunnyQueueEntry> entries)
    {
        PersonalSunnyQueueStore.ReplaceAll(entries);
        runPipeline();
    }

    private static void runPipeline()
    {
        lock (pipeline_lock)
        {
            try
            {
                bakeMissing();
                refit();
            }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] PersonalSunnyService pipeline failed: {e}");
            }
            finally
            {
                IsBaking = false;
                Version++;
            }
        }
    }

    private static void bakeMissing()
    {
        var queue = PersonalSunnyQueueStore.Entries;
        var queueKeys = queue.Select(PersonalSunnyJacKey.From).Distinct().ToList();

        var missing = queueKeys.Where(k => !PersonalSunnyJacStore.TryGet(k, out _)).ToList();

        BakeTotal = missing.Count;
        BakeDone = 0;
        IsBaking = missing.Count > 0;
        Version++;

        if (beatmapManager == null || maniaRuleset == null)
        {
            // Dependencies never resolved (widget never loaded) - nothing to bake with. The fit just
            // runs on whatever was already cached.
            IsBaking = false;
            return;
        }

        foreach (var key in missing)
        {
            bakeOne(key);
            BakeDone++;
            Version++;
        }
    }

    private static void bakeOne(PersonalSunnyJacKey key)
    {
        try
        {
            var local = beatmapManager!.QueryBeatmap(b => b.MD5Hash == key.BeatmapMd5);
            if (local == null)
                return; // Not downloaded locally any more - skip, the fit just won't use this item.

            var working = beatmapManager.GetWorkingBeatmap(local);
            var mods = PersonalSunnyModWhitelist.Reconstruct(key.Rate, key.ChartMod);
            var playable = working.GetPlayableBeatmap(maniaRuleset!, mods, CancellationToken.None);

            var result = PersonalJacobianBaker.Bake(playable, mods);

            PersonalSunnyJacStore.Put(new PersonalSunnyJacEntry(key.BeatmapMd5, key.Rate, key.ChartMod, result.Sr0, result.Jacobian));
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyService bake failed for {key}: {e}");
        }
    }

    private static void refit()
    {
        var queue = PersonalSunnyQueueStore.Entries;
        var queueKeys = queue.Select(PersonalSunnyJacKey.From).ToList();

        PersonalSunnyJacStore.PruneTo(queueKeys.Distinct());

        var y = new List<double>();
        var sr0 = new List<double>();
        var jac = new List<double[]>();

        foreach (var entry in queue)
        {
            if (!PersonalSunnyJacStore.TryGet(PersonalSunnyJacKey.From(entry), out var baked))
                continue;

            double accuracy = Math.Min(entry.Accuracy, 1.0 - 1e-6);
            y.Add(-Math.Log(1.0 - accuracy));
            sr0.Add(baked.Sr0);
            jac.Add(baked.Jacobian);
        }

        var fit = PersonalFitSolver.Solve(y.ToArray(), sr0.ToArray(), jac.ToArray());

        PersonalDiff.Update(PersonalFitSolver.ToRealDeltas(fit.UnitStep));

        Alpha = fit.Alpha;
        Beta = fit.Beta;
        FitRecordCount = y.Count;

        PersonalSunnyFitStore.Save(new PersonalSunnyFitStore.FitResult(fit.UnitStep, fit.Alpha, fit.Beta, y.Count, DateTimeOffset.Now));
    }
}
