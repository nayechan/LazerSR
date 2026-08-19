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
    private const double PP_SECTION_MS = 400.0;
    private const int PP_SMOOTH_WIN = 2;
    private const double PP_THRESHOLD = 0.45;
    private const double PP_HEIGHT_MIN = 0.70;
    private const double PP_REGION_FLOOR = 0.70;

    public static StrainGraphData Calculate(
        IBeatmap beatmap, IReadOnlyList<Mod> mods, CancellationToken token)
    {
        if (beatmap is not ManiaBeatmap)
            return new StrainGraphData([], [], []);

        var raw = new SunnyManiaDifficultyCalculator().GetStrainTimeline(beatmap, mods, token);
        if (raw.Length == 0)
            return new StrainGraphData([], [], []);

        var (times, strains) = SmoothD(raw);
        double[] honeySpots = FindHoneySpots(raw);
        return new StrainGraphData(times, strains, honeySpots);
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

    internal static double[] FindHoneySpots((double Time, double Strain)[] raw)
    {
        if (raw.Length == 0)
            return [];

        var (sectionTimes, sectionStrains) = SectionStrains(raw, PP_SECTION_MS);
        if (sectionStrains.Length <= PP_SMOOTH_WIN * 2)
            return [];

        double[] smoothed = MovingAverage(sectionStrains, PP_SMOOTH_WIN);
        double maxStrain = smoothed.Max();
        if (maxStrain <= 0)
            return [];

        double minProminence = maxStrain * PP_THRESHOLD;
        double minHeight = maxStrain * PP_HEIGHT_MIN;
        double p55 = Percentile(smoothed, 55);
        var segments = new List<(double Start, double End)>();

        foreach (var (peak, prominence) in PeaksWithProminence(smoothed))
        {
            if (prominence < minProminence || smoothed[peak] < minHeight)
                continue;

            double baseLevel = smoothed[peak] - prominence;
            double regionFloor = Math.Max(baseLevel + prominence * PP_REGION_FLOOR, p55);

            int left = peak;
            while (left > 0 && smoothed[left - 1] >= regionFloor)
                left--;

            int right = peak;
            while (right < smoothed.Length - 1 && smoothed[right + 1] >= regionFloor)
                right++;

            segments.Add((sectionTimes[left], sectionTimes[right] + PP_SECTION_MS));
        }

        if (segments.Count == 0)
            return [];

        segments.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(double Start, double End)>();
        foreach (var segment in segments)
        {
            if (merged.Count > 0 && segment.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, segment.End));
            else
                merged.Add(segment);
        }

        var points = new List<double>();
        foreach (var (start, end) in merged)
        {
            for (double t = start; t <= end + 0.001; t += RESAMPLE_MS)
                points.Add(t);
        }

        return [.. points];
    }

    private static (double[] Times, double[] Strains) SectionStrains(
        (double Time, double Strain)[] raw,
        double sectionMs)
    {
        double t0 = raw[0].Time;
        double tLast = raw[^1].Time;
        int count = (int)Math.Ceiling((tLast - t0) / sectionMs) + 2;
        double[] times = new double[count];
        double[] strains = new double[count];

        for (int i = 0; i < count; i++)
            times[i] = t0 + i * sectionMs;

        foreach (var (time, strain) in raw)
        {
            int idx = (int)((time - t0) / sectionMs);
            if ((uint)idx < (uint)count && strain > strains[idx])
                strains[idx] = strain;
        }

        return (times, strains);
    }

    private static double[] MovingAverage(double[] values, int window)
    {
        double[] result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            int lo = Math.Max(0, i - window);
            int hi = Math.Min(values.Length - 1, i + window);
            double sum = 0;
            for (int j = lo; j <= hi; j++)
                sum += values[j];
            result[i] = sum / (hi - lo + 1);
        }
        return result;
    }

    private static IEnumerable<(int Peak, double Prominence)> PeaksWithProminence(double[] values)
    {
        for (int p = 1; p < values.Length - 1; p++)
        {
            if (values[p] < values[p - 1] || values[p] < values[p + 1])
                continue;

            double leftMin = values[p];
            for (int i = p - 1; i >= 0; i--)
            {
                if (values[i] > values[p])
                    break;
                if (values[i] < leftMin)
                    leftMin = values[i];
            }

            double rightMin = values[p];
            for (int i = p + 1; i < values.Length; i++)
            {
                if (values[i] > values[p])
                    break;
                if (values[i] < rightMin)
                    rightMin = values[i];
            }

            yield return (p, Math.Max(0.0, values[p] - Math.Max(leftMin, rightMin)));
        }
    }

    private static double[] GaussianSmooth(double[] values, double sigma)
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

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
            return 0;

        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        double pos = (percentile / 100.0) * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi)
            return sorted[lo];
        double f = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * f;
    }
}
