namespace LazerSR.Hook.Calculators;

// Classic (stable) osu!mania judgement windows, OD-based. 300g is fixed at 16ms
// regardless of OD; the rest follow the well-known 64/97/127/151/188 - 3*OD formula.
// Returns the accuracy numerator (out of 300) for a given raw hit-error offset.
internal static class ClassicManiaAccuracy
{
    public static int Judge(double absOffsetMs, double overallDifficulty)
    {
        if (absOffsetMs <= 16) return 300;
        if (absOffsetMs <= 64 - 3 * overallDifficulty) return 300;
        if (absOffsetMs <= 97 - 3 * overallDifficulty) return 200;
        if (absOffsetMs <= 127 - 3 * overallDifficulty) return 100;
        if (absOffsetMs <= 151 - 3 * overallDifficulty) return 50;
        return 0;
    }
}
