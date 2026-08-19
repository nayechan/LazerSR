using System;
using System.Collections.Generic;
using System.Threading;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace LazerSR.Hook.Calculators;

public static class PPCurveAnalyzer
{
    // Segment splits when the new slope deviates from the running mean by this fraction.
    private const double SPLIT_THRESHOLD = 0.40;
    private const int SLOPE_SMOOTH = 3;
    private const double RESAMPLE_MS = 100.0;

    /// <summary>
    /// Builds a PP curve (all-320s assumption) from timed attributes,
    /// segments it by slope similarity, and returns time points in segments
    /// whose average slope >= maxAvgSlope * (thresholdPct / 100).
    /// </summary>
    public static double[] Analyze(
        List<TimedDifficultyAttributes> timedAttrs,
        PerformanceCalculator calc,
        float thresholdPct,
        float ppWeightExponent,
        CancellationToken token)
    {
        int n = timedAttrs.Count;
        if (n < 4) return [];

        // 1. Build PP curve assuming all 320s.
        double[] times = new double[n];
        double[] pp    = new double[n];

        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            times[i] = timedAttrs[i].Time;
            int combo = Math.Max(1, timedAttrs[i].Attributes.MaxCombo);
            var si = new ScoreInfo
            {
                Accuracy = 1.0,
                MaxCombo = combo,
                Statistics = new Dictionary<HitResult, int> { [HitResult.Perfect] = combo },
            };
            pp[i] = calc.Calculate(si, timedAttrs[i].Attributes).Total;
        }

        // 2. Centred-difference slope, then box-smooth.
        int k = Math.Max(1, n / 50);
        double[] rawSlopes = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - k);
            int hi = Math.Min(n - 1, i + k);
            double dt = times[hi] - times[lo];
            rawSlopes[i] = dt > 0 ? (pp[hi] - pp[lo]) / dt : 0.0;
        }
        double[] slopes = BoxSmooth(rawSlopes, SLOPE_SMOOTH);

        // Apply PP-level weighting: segments at higher pp get a proportionally larger slope.
        double ppMax = pp[n - 1];
        if (ppWeightExponent > 0 && ppMax > 0)
        {
            for (int i = 0; i < n; i++)
                slopes[i] *= Math.Pow(pp[i] / ppMax, ppWeightExponent);
        }

        // 3. Greedy forward segmentation by relative slope deviation.
        var segments = new List<(double Start, double End, double AvgSlope)>();
        int segStart  = 0;
        double slopeSum = slopes[0];
        int segCount  = 1;

        for (int i = 1; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            double segMean = slopeSum / segCount;
            bool split = segMean > 1e-9
                ? Math.Abs(slopes[i] - segMean) > SPLIT_THRESHOLD * segMean
                : slopes[i] > 1e-9;

            if (split)
            {
                segments.Add((times[segStart], times[i - 1], segMean));
                segStart  = i;
                slopeSum  = slopes[i];
                segCount  = 1;
            }
            else
            {
                slopeSum += slopes[i];
                segCount++;
            }
        }
        segments.Add((times[segStart], times[n - 1], slopeSum / segCount));

        // 4. Keep segments whose average slope >= maxAvgSlope * threshold.
        double maxAvg = 0;
        foreach (var (_, _, avg) in segments)
            if (avg > maxAvg) maxAvg = avg;

        if (maxAvg <= 0) return [];

        double minSlope = maxAvg * (thresholdPct / 100.0);
        var points = new List<double>();
        foreach (var (start, end, avg) in segments)
        {
            if (avg < minSlope) continue;
            for (double t = start; t <= end + 0.001; t += RESAMPLE_MS)
                points.Add(t);
        }

        return [.. points];
    }

    private static double[] BoxSmooth(double[] v, int w)
    {
        double[] out_ = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            int lo = Math.Max(0, i - w), hi = Math.Min(v.Length - 1, i + w);
            double s = 0;
            for (int j = lo; j <= hi; j++) s += v[j];
            out_[i] = s / (hi - lo + 1);
        }
        return out_;
    }
}
