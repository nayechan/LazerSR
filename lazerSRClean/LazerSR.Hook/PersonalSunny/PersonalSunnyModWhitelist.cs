using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace LazerSR.Hook.PersonalSunny;

/// <summary>
/// The personal-diff mod whitelist: NM/DT/HT/NC/DC (any rate, lazer's fine-grained speed change
/// included) and HO/IN (mutually exclusive chart rewrites - see architecture.md's mania mod research
/// for why these need a mod signature at all: unlike the rate mods they rewrite <c>HitObjects</c>
/// itself, so a Jacobian baked for one map+rate is wrong for the other's chart).
///
/// Anything else present (HD, FL, HR, EZ, Mirror, Random, ...) disqualifies the score outright -
/// deliberately strict, since the whole point is a clean accuracy signal against a chart sunny
/// actually saw.
/// </summary>
public static class PersonalSunnyModWhitelist
{
    private static readonly HashSet<string> allowed_acronyms = new() { "DT", "HT", "NC", "DC", "HO", "IN" };

    /// <summary>True if every mod on the score is in the whitelist.</summary>
    public static bool IsAllowed(IReadOnlyList<Mod> mods) => mods.All(m => allowed_acronyms.Contains(m.Acronym));

    /// <summary>The exact rate (1.0 if no rate mod) and the chart mod acronym ("HO"/"IN"), or null.</summary>
    public static (double Rate, string? ChartMod) Describe(IReadOnlyList<Mod> mods)
    {
        double rate = ModUtils.CalculateRateWithMods(mods);
        string? chartMod = mods.FirstOrDefault(m => m.Acronym is "HO" or "IN")?.Acronym;

        return (rate, chartMod);
    }

    /// <summary>
    /// Rebuilds a mod list from a stored (rate, chart mod) pair, for baking. Which literal rate mod
    /// (DT vs NC, HT vs DC) produced the rate doesn't matter - only <see cref="ModUtils.CalculateRateWithMods"/>'s
    /// result does, so this always picks the DT/HT family by sign.
    /// </summary>
    public static IReadOnlyList<Mod> Reconstruct(double rate, string? chartMod)
    {
        var mods = new List<Mod>();

        if (rate > 1.0)
            mods.Add(new ManiaModDoubleTime { SpeedChange = { Value = Math.Clamp(rate, 1.01, 2.0) } });
        else if (rate < 1.0)
            mods.Add(new ManiaModHalfTime { SpeedChange = { Value = Math.Clamp(rate, 0.5, 0.99) } });

        if (chartMod == "HO")
            mods.Add(new ManiaModHoldOff());
        else if (chartMod == "IN")
            mods.Add(new ManiaModInvert());

        return mods;
    }
}
