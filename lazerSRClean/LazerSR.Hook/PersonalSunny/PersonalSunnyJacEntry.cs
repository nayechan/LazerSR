namespace LazerSR.Hook.PersonalSunny;

/// <summary>One baked Jacobian - SR at the universal point plus the 11-slot unit-space slope, for one <see cref="PersonalSunnyJacKey"/>.</summary>
public record PersonalSunnyJacEntry(string BeatmapMd5, double Rate, string? ChartMod, double Sr0, double[] Jacobian)
{
    public PersonalSunnyJacKey Key => new(BeatmapMd5, Rate, ChartMod);
}
