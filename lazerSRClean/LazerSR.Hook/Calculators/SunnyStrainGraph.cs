using System.Collections.Generic;
using System.Threading;
using LazerSR.Hook.Data;
using LazerSR.SunnyCalculator;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace LazerSR.Hook.Calculators;

public static class SunnyStrainGraph
{
    private const double RESAMPLE_MS = 100.0;
    private const double BREAK_ZERO_THRESHOLD_MS = 400.0;
    private const double SMOOTH_SIGMA_MS = 800.0;
    public static StrainGraphData Calculate(
        IBeatmap beatmap, IReadOnlyList<Mod> mods, CancellationToken token)
    {
        if (beatmap is not ManiaBeatmap)
            return new StrainGraphData([], []);

        var raw = new SunnyManiaDifficultyCalculator().GetStrainTimeline(beatmap, mods, token);
        if (raw.Length == 0)
            return new StrainGraphData([], []);

        var (times, strains) = SmoothD(raw);
        return new StrainGraphData(times, strains);
    }

    internal static (double[] Times, double[] Strains) SmoothD(
        (double Time, double Strain)[] raw)
    {
        if (raw.Length == 0)
            return ([], []);

        double minTime = raw[0].Time;
        double maxTime = raw[^1].Time;
        int count = (int)Math.Ceiling((maxTime - minTime) / RESAMPLE_MS) + 1;

        double[] rawTimes = raw.Select(p => p.Time).ToArray();
        double[] rawStrains = raw.Select(p => p.Strain).ToArray();
        double[] uniform = new double[count];
        bool[] breakMask = new bool[count];

        for (int i = 0; i < count; i++)
        {
            double t = minTime + i * RESAMPLE_MS;
            uniform[i] = Interpolate(t, rawTimes, rawStrains);
            breakMask[i] = DistanceToNearest(t, rawTimes) > BREAK_ZERO_THRESHOLD_MS;
            if (breakMask[i])
                uniform[i] = 0.0;
        }

        double[] times   = new double[count];
        double[] strains = new double[count];
        double[] smoothed = GaussianSmooth(uniform, SMOOTH_SIGMA_MS / RESAMPLE_MS);

        for (int i = 0; i < count; i++)
        {
            times[i] = minTime + i * RESAMPLE_MS;
            strains[i] = breakMask[i] ? 0.0 : smoothed[i];
        }

        return (times, strains);
    }

    internal static double[] GaussianSmooth(double[] values, double sigma)
    {
        if (values.Length == 0)
            return [];

        int radius = (int)(4 * sigma + 0.5);
        double[] kernel = new double[radius * 2 + 1];
        double sum = 0;
        for (int i = 0; i < kernel.Length; i++)
        {
            double x = i - radius;
            double v = Math.Exp(-0.5 * (x / sigma) * (x / sigma));
            kernel[i] = v;
            sum += v;
        }
        for (int i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;

        double[] result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double acc = 0;
            for (int k = 0; k < kernel.Length; k++)
            {
                int src = i + k - radius;
                if ((uint)src < (uint)values.Length)
                    acc += values[src] * kernel[k];
            }
            result[i] = acc;
        }
        return result;
    }

    private static double Interpolate(double t, double[] times, double[] values)
    {
        int idx = Array.BinarySearch(times, t);
        if (idx >= 0)
            return values[idx];

        idx = ~idx;
        if (idx <= 0)
            return values[0];
        if (idx >= times.Length)
            return values[^1];

        double span = times[idx] - times[idx - 1];
        if (span <= 0)
            return values[idx];

        double f = (t - times[idx - 1]) / span;
        return values[idx - 1] + (values[idx] - values[idx - 1]) * f;
    }

    private static double DistanceToNearest(double t, double[] times)
    {
        int idx = Array.BinarySearch(times, t);
        if (idx >= 0)
            return 0;

        idx = ~idx;
        double best = double.PositiveInfinity;
        if (idx < times.Length)
            best = Math.Min(best, Math.Abs(times[idx] - t));
        if (idx > 0)
            best = Math.Min(best, Math.Abs(times[idx - 1] - t));
        return best;
    }

}
