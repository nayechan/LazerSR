using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Scoring;

namespace LazerSR.Hook.Calculators;

internal static class BestScoreFinder
{
    internal static double? FindBestAccuracy(
        RealmAccess realm,
        string beatmapHash,
        APIMod[] currentMods,
        string? usernameFilter)
    {
        double? result = null;

        realm.Run(r =>
        {
            var list = r.All<ScoreInfo>()
                .Where(s => !s.DeletePending && s.BeatmapHash == beatmapHash)
                .ToList();

            var best = list
                .Where(s => string.IsNullOrEmpty(usernameFilter) || s.RealmUser.Username == usernameFilter)
                .Where(s => ApiModSetsEqual(s.APIMods, currentMods))
                .OrderByDescending(s => s.TotalScore)
                .FirstOrDefault();

            result = best?.Accuracy;
        });

        return result;
    }

    internal static string? FindBestReplayPath(
        RealmAccess realm,
        Storage storage,
        string beatmapHash,
        APIMod[] currentMods,
        string? usernameFilter)
    {
        string? result = null;

        realm.Run(r =>
        {
            var list = r.All<ScoreInfo>()
                .Where(s => !s.DeletePending && s.BeatmapHash == beatmapHash)
                .ToList();

            var best = list
                .Where(s => string.IsNullOrEmpty(usernameFilter) || s.RealmUser.Username == usernameFilter)
                .Where(s => ApiModSetsEqual(s.APIMods, currentMods))
                .OrderByDescending(s => s.TotalScore)
                .FirstOrDefault();

            if (best == null) return;

            var replayFile = best.Files.FirstOrDefault(
                f => f.Filename.EndsWith(".osr", StringComparison.OrdinalIgnoreCase));
            if (replayFile == null) return;

            var filesStorage = storage.GetStorageForDirectory("files");
            result = filesStorage.GetFullPath(replayFile.File.GetStoragePath());
        });

        return result;
    }

    private static bool ApiModSetsEqual(APIMod[] scoreMods, APIMod[] currentMods)
    {
        // Mirror(MR) 제외
        var s = scoreMods.Where(m => m.Acronym != "MR").ToArray();
        var c = currentMods.Where(m => m.Acronym != "MR").ToArray();
        if (s.Length != c.Length) return false;
        foreach (var cm in c)
        {
            if (!s.Any(sm => ModMatch(sm, cm))) return false;
        }
        return true;
    }

    // NC↔DT, DC↔HT를 같은 군으로 정규화
    private static string RateGroup(string acronym) => acronym switch
    {
        "NC" => "DT",
        "DC" => "HT",
        _ => acronym,
    };

    private static bool ModMatch(APIMod a, APIMod b)
    {
        if (RateGroup(a.Acronym) != RateGroup(b.Acronym)) return false;
        // 같은 군이고 acronym도 동일 → 기존 Equals (설정 전체 비교)
        if (a.Acronym == b.Acronym) return a.Equals(b);
        // 같은 군이지만 acronym이 다른 경우 (DT vs NC, HT vs DC) → speed_change만 비교
        return GetSpeedChange(a.Settings) == GetSpeedChange(b.Settings);
    }

    private static double? GetSpeedChange(IReadOnlyDictionary<string, object> settings)
        => settings.TryGetValue("speed_change", out var v) && v is double d ? d : null;
}
