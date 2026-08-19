using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;

namespace LazerSR.Hook.Calculators;

// BPM = 15000 / delta_ms (quarter-note basis, same as Companella PatternFinder)
public static class PatternBpmCalculator
{
    private const double MaxIntervalMs = 250.0;
    private const double MinijackMaxMs = 100.0;
    private const double GroupToleranceMs = 2.0;
    private const int MinStreamNotes = 4;
    private const int MinJackNotes = 3;
    private const int MinChordstreamNotes = 6;

    public static int GetMaxBpm(IBeatmap beatmap, string dominantAbbr)
    {
        if (beatmap is not ManiaBeatmap mb || mb.TotalColumns != 4) return 0;
        if (string.IsNullOrEmpty(dominantAbbr) || dominantAbbr == "N/A") return 0;

        var notes = ExtractNotes(mb);
        if (notes.Count < 2) return 0;

        double bpm = dominantAbbr switch
        {
            "STR" => StreamMaxBpm(notes),
            "JS"  => ChordstreamMaxBpm(notes, 2),
            "HS"  => ChordstreamMaxBpm(notes, 3),
            "JK"  => JackMaxBpm(notes),
            "CJ"  => ChordjackMaxBpm(notes),
            "TEC" => MinijackMaxBpm(notes),
            _     => 0
        };

        return (int)Math.Round(bpm);
    }

    private static List<(double Time, int Column)> ExtractNotes(ManiaBeatmap mb)
    {
        var list = new List<(double Time, int Column)>(mb.HitObjects.Count);
        foreach (var obj in mb.HitObjects)
            if (obj is ManiaHitObject mho)
                list.Add((mho.StartTime, mho.Column));
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list;
    }

    // Groups consecutive notes within GroupToleranceMs into chord events (notes are pre-sorted)
    private static List<(double Time, List<int> Columns)> GroupByTime(List<(double Time, int Column)> notes)
    {
        var groups = new List<(double Time, List<int> Columns)>();
        foreach (var (time, col) in notes)
        {
            if (groups.Count > 0 && Math.Abs(groups[^1].Time - time) <= GroupToleranceMs)
                groups[^1].Columns.Add(col);
            else
                groups.Add((time, new List<int> { col }));
        }
        return groups;
    }

    private static double BpmFromDelta(double deltaMs)
        => deltaMs > 0 ? 15000.0 / deltaMs : 0;

    private static double BpmFromTimes(List<double> times)
    {
        if (times.Count < 2) return 0;
        double sum = 0;
        for (int i = 1; i < times.Count; i++) sum += times[i] - times[i - 1];
        return BpmFromDelta(sum / (times.Count - 1));
    }

    // STR: consecutive single-note events on different columns
    private static double StreamMaxBpm(List<(double Time, int Column)> notes)
    {
        var groups = GroupByTime(notes);
        var singles = groups.Where(g => g.Columns.Distinct().Count() == 1)
                            .Select(g => (Time: g.Time, Col: g.Columns[0])).ToList();

        double max = 0;
        if (singles.Count < MinStreamNotes) return 0;

        int lastCol = singles[0].Col;
        var seg = new List<double> { singles[0].Time };

        for (int i = 1; i < singles.Count; i++)
        {
            var (time, col) = singles[i];
            double interval = time - singles[i - 1].Time;
            if (col != lastCol && interval <= MaxIntervalMs && interval > 0)
            {
                lastCol = col;
                seg.Add(time);
            }
            else
            {
                if (seg.Count >= MinStreamNotes) max = Math.Max(max, BpmFromTimes(seg));
                lastCol = col;
                seg = new List<double> { time };
            }
        }
        if (seg.Count >= MinStreamNotes) max = Math.Max(max, BpmFromTimes(seg));
        return max;
    }

    // JS (minChordSize=2) / HS (minChordSize=3): chord stream with required chord size
    private static double ChordstreamMaxBpm(List<(double Time, int Column)> notes, int minChordSize)
    {
        var groups = GroupByTime(notes);
        double max = 0;
        int noteCount = 0, chordCount = 0;
        var seg = new List<double>();

        for (int i = 0; i < groups.Count; i++)
        {
            var (time, cols) = groups[i];
            int size = cols.Distinct().Count();
            double interval = i > 0 ? time - groups[i - 1].Time : 0;

            if (interval <= MaxIntervalMs || i == 0)
            {
                noteCount += size;
                seg.Add(time);
                if (size >= minChordSize) chordCount++;
            }
            else
            {
                if (noteCount >= MinChordstreamNotes && chordCount > 0)
                    max = Math.Max(max, BpmFromTimes(seg));
                noteCount = size;
                chordCount = size >= minChordSize ? 1 : 0;
                seg = new List<double> { time };
            }
        }
        if (noteCount >= MinChordstreamNotes && chordCount > 0)
            max = Math.Max(max, BpmFromTimes(seg));
        return max;
    }

    // JK: same-column repeated notes
    private static double JackMaxBpm(List<(double Time, int Column)> notes)
    {
        var byCol = notes.GroupBy(n => n.Column)
                         .ToDictionary(g => g.Key, g => g.Select(n => n.Time).ToList());
        double max = 0;

        foreach (var times in byCol.Values)
        {
            var seg = new List<double> { times[0] };
            for (int i = 1; i < times.Count; i++)
            {
                double interval = times[i] - times[i - 1];
                if (interval <= MaxIntervalMs && interval > 0)
                    seg.Add(times[i]);
                else
                {
                    if (seg.Count >= MinJackNotes) max = Math.Max(max, BpmFromTimes(seg));
                    seg = new List<double> { times[i] };
                }
            }
            if (seg.Count >= MinJackNotes) max = Math.Max(max, BpmFromTimes(seg));
        }
        return max;
    }

    // CJ: consecutive chords with at least one shared column
    private static double ChordjackMaxBpm(List<(double Time, int Column)> notes)
    {
        var groups = GroupByTime(notes);
        var chords = groups.Where(g => g.Columns.Distinct().Count() >= 2).ToList();
        if (chords.Count < 3) return 0;

        double max = 0;
        var seg = new List<double> { chords[0].Time };

        for (int i = 1; i < chords.Count; i++)
        {
            double interval = chords[i].Time - chords[i - 1].Time;
            int overlap = chords[i].Columns.Intersect(chords[i - 1].Columns).Count();
            if (interval <= MaxIntervalMs && overlap > 0)
                seg.Add(chords[i].Time);
            else
            {
                if (seg.Count >= 3) max = Math.Max(max, BpmFromTimes(seg));
                seg = new List<double> { chords[i].Time };
            }
        }
        if (seg.Count >= 3) max = Math.Max(max, BpmFromTimes(seg));
        return max;
    }

    // TEC: minijack — same column within 100ms
    private static double MinijackMaxBpm(List<(double Time, int Column)> notes)
    {
        var byCol = notes.GroupBy(n => n.Column)
                         .ToDictionary(g => g.Key, g => g.Select(n => n.Time).ToList());
        double max = 0;

        foreach (var times in byCol.Values)
            for (int i = 1; i < times.Count; i++)
            {
                double delta = times[i] - times[i - 1];
                if (delta > 0 && delta <= MinijackMaxMs)
                    max = Math.Max(max, BpmFromDelta(delta));
            }
        return max;
    }
}
