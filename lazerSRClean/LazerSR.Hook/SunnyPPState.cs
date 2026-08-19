using System;

namespace LazerSR.Hook;

/// <summary>
/// Published live by <see cref="Widgets.SunnyPPWidget"/> during gameplay. Same contract as
/// <see cref="ClassicAccuracyState"/> - only meaningful for the exact score it was computed
/// while watching/playing; the results screen must only display it when the panel's score
/// matches <see cref="ScoreId"/>, showing a placeholder for every other score.
/// </summary>
public static class SunnyPPState
{
    public static Guid? ScoreId;
    public static int? Pp;
}
