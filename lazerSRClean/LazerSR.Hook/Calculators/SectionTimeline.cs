using System;
using System.Collections.Generic;
using System.Threading;
using LazerSR.Hook.Data;

namespace LazerSR.Hook.Calculators;

public static class SectionTimeline
{
    private const double EXTRA_SMOOTH_SIGMA_MS = 1100.0;
    private const double RESAMPLE_MS = 100.0;

    public static SectionInterval[] Calculate(
        (double Time, double Strain)[] raw,
        double hardThresholdFraction = 0.75,
        double minSegmentMs = 4000.0,
        CancellationToken token = default)
    {
        if (raw.Length == 0) return [];

        var (times, strains) = SunnyStrainGraph.SmoothD(raw);
        if (times.Length == 0) return [];

        token.ThrowIfCancellationRequested();

        double max = 0;
        for (int i = 0; i < strains.Length; i++)
            if (strains[i] > max) max = strains[i];
        if (max <= 0) return [];

        double gamma = 3.0 / 2.2;
        double[] gammaArr = new double[strains.Length];
        for (int i = 0; i < strains.Length; i++)
            gammaArr[i] = Math.Pow(strains[i] / max, gamma);

        token.ThrowIfCancellationRequested();

        double[] smoothed = SunnyStrainGraph.GaussianSmooth(gammaArr, EXTRA_SMOOTH_SIGMA_MS / RESAMPLE_MS);

        double smoothedMax = 0;
        for (int i = 0; i < smoothed.Length; i++)
            if (smoothed[i] > smoothedMax) smoothedMax = smoothed[i];
        if (smoothedMax <= 0) return [];

        double threshold = smoothedMax * hardThresholdFraction;

        var segments = new List<SectionInterval>();
        int segStart = 0;
        bool segIsHard = smoothed[0] >= threshold;
        for (int i = 1; i <= times.Length; i++)
        {
            bool curIsHard = i < times.Length && smoothed[i] >= threshold;
            if (i == times.Length || curIsHard != segIsHard)
            {
                double endMs = i < times.Length ? times[i] : times[i - 1] + RESAMPLE_MS;
                segments.Add(new SectionInterval(times[segStart], endMs, segIsHard));
                if (i < times.Length) { segStart = i; segIsHard = curIsHard; }
            }
        }

        token.ThrowIfCancellationRequested();

        return MergeShortSegments(segments, minSegmentMs);
    }

    private static SectionInterval[] MergeShortSegments(List<SectionInterval> segs, double minMs)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;

            for (int i = segs.Count - 1; i > 0; i--)
            {
                if (segs[i].IsHard == segs[i - 1].IsHard)
                {
                    segs[i - 1] = new SectionInterval(segs[i - 1].StartMs, segs[i].EndMs, segs[i - 1].IsHard);
                    segs.RemoveAt(i);
                    changed = true;
                }
            }

            for (int i = 0; i < segs.Count; i++)
            {
                if (segs[i].EndMs - segs[i].StartMs <= minMs)
                {
                    if (segs.Count == 1) break;

                    bool absorbToPrev = i == segs.Count - 1 || (i > 0 &&
                        (segs[i - 1].EndMs - segs[i - 1].StartMs) >= (segs[i + 1].EndMs - segs[i + 1].StartMs));

                    if (absorbToPrev)
                    {
                        segs[i - 1] = new SectionInterval(segs[i - 1].StartMs, segs[i].EndMs, segs[i - 1].IsHard);
                        segs.RemoveAt(i);
                    }
                    else
                    {
                        segs[i + 1] = new SectionInterval(segs[i].StartMs, segs[i + 1].EndMs, segs[i + 1].IsHard);
                        segs.RemoveAt(i);
                    }

                    changed = true;
                    break;
                }
            }
        }

        return segs.ToArray();
    }
}
