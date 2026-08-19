using osu.Framework.Bindables;
using osu.Game.Beatmaps;

namespace LazerSR.Hook;

public static class SunnyState
{
    private static readonly object _lock = new();

    public static bool Enabled { get; private set; }

    /// <summary>
    /// Shared publish/subscribe channel for current sunnySR.
    /// Orchestrator (DifficultyDisplayPatch) sets the value after each calculation.
    /// Per-location pill patches bind their StarRatingDisplay.Current to this.
    /// </summary>
    public static readonly Bindable<StarDifficulty> CurrentSr = new();

    /// <summary>
    /// Dominant MSD skillset abbreviation for the current beatmap (e.g. "JS", "CJ").
    /// Set by DifficultyDisplayPatch after filtered MSD calculation.
    /// Empty string when no mania map is selected.
    /// </summary>
    public static readonly Bindable<string> CurrentDominant = new(string.Empty);

    public static void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            Enabled = enabled;
        }
    }
}
