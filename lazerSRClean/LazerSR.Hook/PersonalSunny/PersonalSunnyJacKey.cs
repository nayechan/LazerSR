using System.Globalization;

namespace LazerSR.Hook.PersonalSunny;

/// <summary>
/// Identifies a baked Jacobian: a beatmap at an exact rate, with or without a chart-rewriting mod.
/// Rate-only mods (DT/HT/NC/DC) don't change the key beyond <see cref="Rate"/> - which literal mod
/// produced that rate doesn't matter, only the resulting speed does.
/// </summary>
public readonly record struct PersonalSunnyJacKey(string BeatmapMd5, double Rate, string? ChartMod)
{
    public static PersonalSunnyJacKey From(PersonalSunnyQueueEntry entry) => new(entry.BeatmapMd5, entry.Rate, entry.ChartMod);

    /// <summary>Stable string form for use as a dictionary/JSON key.</summary>
    public override string ToString() => $"{BeatmapMd5}|{Rate.ToString("R", CultureInfo.InvariantCulture)}|{ChartMod ?? "-"}";
}
