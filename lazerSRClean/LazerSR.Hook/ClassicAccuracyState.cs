using System;

namespace LazerSR.Hook;

/// <summary>
/// Published live by <see cref="Widgets.ClassicAccuracyWidget"/> during judgement processing
/// (in-gameplay HUD). This value only has meaning for the exact score it was computed while
/// watching/playing - the results screen reads it and must only display it when the panel's
/// score matches <see cref="ScoreId"/>, showing a placeholder for every other score.
/// </summary>
public static class ClassicAccuracyState
{
    public static Guid? ScoreId;
    public static double? Accuracy;
}
