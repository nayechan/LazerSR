using System;

namespace LazerSR.Hook.PersonalSunny;

/// <summary>
/// One queued score for the personal-diff fit: a mania 4K nomod/rate-mod pass by the local player.
/// <see cref="Rate"/> is the exact playback rate (lazer's fine-grained rate adjust included, not just
/// 1.5/0.75) and <see cref="ChartMod"/> is "HO"/"IN" when the chart itself was rewritten by
/// Hold Off/Invert, or null otherwise - together with <see cref="BeatmapMd5"/> they are exactly what a
/// baked Jacobian is keyed on (<see cref="PersonalSunnyJacKey"/>), since HO/IN change the chart and DT/HT/NC/DC don't.
/// </summary>
public record PersonalSunnyQueueEntry(
    string BeatmapMd5,
    double Rate,
    string? ChartMod,
    double Accuracy,
    DateTimeOffset EndedAt);
