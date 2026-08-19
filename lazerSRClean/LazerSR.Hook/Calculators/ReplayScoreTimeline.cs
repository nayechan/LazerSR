using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LazerSR.Hook.Data;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace LazerSR.Hook.Calculators;

// Simulates a .osr replay against a beatmap by directly using osu! lazer's own
// ManiaHitWindows + ManiaScoreProcessor + OrderedHitPolicy semantics.
// Hit windows come from the beatmap's HitObjects (mod multipliers already baked in
// by ManiaModHardRock / IManiaRateAdjustmentMod via GetPlayableBeatmap).
// Score is computed by ManiaScoreProcessor.ApplyResult, so it matches in-game scoring.
internal static class ReplayScoreTimeline
{
    public static ReplayFrame[] Calculate(IBeatmap beatmap, string osrPath, out string replayDate)
    {
        var (keyEvents, date) = ParseReplay(osrPath);
        replayDate = date;

        var sp = new ManiaScoreProcessor();
        sp.ApplyBeatmap(beatmap);

        // Press-queue per column. Each HoldNote contributes its Head; Tails are handled on release.
        var columns = new Dictionary<int, List<ManiaHitObject>>();
        var holdOf  = new Dictionary<ManiaHitObject, HoldNote>(); // head → parent HoldNote

        foreach (var ho in beatmap.HitObjects)
        {
            switch (ho)
            {
                case HoldNote hn:
                    addToColumn(columns, hn.Column, hn.Head);
                    holdOf[hn.Head] = hn;
                    break;
                case ManiaHitObject mn:
                    addToColumn(columns, mn.Column, mn);
                    break;
            }
        }
        foreach (var l in columns.Values)
            l.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        var ptrs     = columns.ToDictionary(kv => kv.Key, _ => 0);
        var activeLn = new Dictionary<int, HoldNote>();
        var pending  = new List<PendingResult>();

        foreach (var (t, col, isPress) in keyEvents)
        {
            if (!columns.TryGetValue(col, out var list)) continue;
            int ptr = ptrs[col];

            if (isPress)
            {
                // 1) Auto-miss any notes whose Miss window has fully elapsed.
                while (ptr < list.Count)
                {
                    var obj = list[ptr];
                    if (t - obj.StartTime <= obj.HitWindows.WindowFor(HitResult.Miss)) break;
                    missNote(obj, holdOf, pending);
                    ptr++;
                }
                // 2) OrderedHitPolicy: a note becomes unhittable once the *next* note in the column has started.
                while (ptr < list.Count - 1 && t >= list[ptr + 1].StartTime)
                {
                    missNote(list[ptr], holdOf, pending);
                    ptr++;
                }
                ptrs[col] = ptr;
                if (ptr >= list.Count) continue;

                var cand = list[ptr];
                double offset = t - cand.StartTime;
                var hr = cand.HitWindows.ResultFor(offset);
                if (hr == HitResult.None) continue; // too early — ignore the press

                pending.Add(new PendingResult(t, makeResult(cand, hr), countsAsJudgment: true));
                ptrs[col] = ptr + 1;

                if (holdOf.TryGetValue(cand, out var hnPressed))
                    activeLn[col] = hnPressed;
            }
            else // release
            {
                if (!activeLn.Remove(col, out var hn)) continue;

                double leniencedOffset = (t - hn.Tail.StartTime) / TailNote.RELEASE_WINDOW_LENIENCE;
                var tr = hn.Tail.HitWindows.ResultFor(leniencedOffset);
                if (tr == HitResult.None) tr = HitResult.Miss;

                // GetCappedResult: head missed → cap tail at Meh (osu! DrawableHoldNoteTail.GetCappedResult)
                bool headHit = pending.Any(p => ReferenceEquals(p.Result.HitObject, hn.Head) && p.Result.IsHit);
                if (!headHit && tr > HitResult.Meh) tr = HitResult.Meh;

                bool tailHit = tr.IsHit();

                pending.Add(new PendingResult(t, makeResult(hn.Tail, tr),                                                    countsAsJudgment: true));
                pending.Add(new PendingResult(t, makeResult(hn.Body, tailHit ? HitResult.IgnoreHit : HitResult.ComboBreak),  countsAsJudgment: false));
                pending.Add(new PendingResult(t, makeResult(hn,      tailHit ? HitResult.IgnoreHit : HitResult.Miss),        countsAsJudgment: false));
            }
        }

        // Notes never pressed → Miss
        foreach (var kv in columns)
            for (int i = ptrs[kv.Key]; i < kv.Value.Count; i++)
                missNote(kv.Value[i], holdOf, pending);

        // Still-held LNs (no release recorded) → tail Miss + body ComboBreak + HoldNote Miss
        foreach (var kv in activeLn)
        {
            var hn = kv.Value;
            double endT = hn.Tail.StartTime + hn.Tail.HitWindows.WindowFor(HitResult.Miss) * TailNote.RELEASE_WINDOW_LENIENCE;
            pending.Add(new PendingResult(endT, makeResult(hn.Tail, HitResult.Miss),       countsAsJudgment: true));
            pending.Add(new PendingResult(endT, makeResult(hn.Body, HitResult.ComboBreak), countsAsJudgment: false));
            pending.Add(new PendingResult(endT, makeResult(hn,      HitResult.Miss),       countsAsJudgment: false));
        }

        // Apply chronologically (stable sort preserves Tail→Body→HoldNote sequence at equal times)
        var ordered = pending.OrderBy(p => p.Time).ToList();

        var frames = new List<ReplayFrame>(ordered.Count);
        int j320 = 0, j300 = 0, j200 = 0, j100 = 0, j50 = 0, jMiss = 0;

        foreach (var p in ordered)
        {
            sp.ApplyResult(p.Result);
            if (!p.CountsAsJudgment) continue;

            switch (p.Result.Type)
            {
                case HitResult.Perfect: j320++; break;
                case HitResult.Great:   j300++; break;
                case HitResult.Good:    j200++; break;
                case HitResult.Ok:      j100++; break;
                case HitResult.Meh:     j50++;  break;
                case HitResult.Miss:    jMiss++; break;
            }
            frames.Add(new ReplayFrame(p.Time, (int)sp.TotalScore.Value, sp.Combo.Value,
                j320, j300, j200, j100, j50, jMiss));
        }

        return frames.ToArray();
    }

    private readonly struct PendingResult
    {
        public readonly double Time;
        public readonly JudgementResult Result;
        public readonly bool CountsAsJudgment;
        public PendingResult(double t, JudgementResult r, bool countsAsJudgment)
        {
            Time = t; Result = r; CountsAsJudgment = countsAsJudgment;
        }
    }

    private static void addToColumn(Dictionary<int, List<ManiaHitObject>> dict, int col, ManiaHitObject obj)
    {
        if (!dict.TryGetValue(col, out var list)) dict[col] = list = new List<ManiaHitObject>();
        list.Add(obj);
    }

    private static void missNote(ManiaHitObject obj, Dictionary<ManiaHitObject, HoldNote> holdOf, List<PendingResult> pending)
    {
        double missTime = obj.StartTime + obj.HitWindows.WindowFor(HitResult.Miss);
        pending.Add(new PendingResult(missTime, makeResult(obj, HitResult.Miss), countsAsJudgment: true));

        if (holdOf.TryGetValue(obj, out var hn))
        {
            double tailT = hn.Tail.StartTime + hn.Tail.HitWindows.WindowFor(HitResult.Miss) * TailNote.RELEASE_WINDOW_LENIENCE;
            pending.Add(new PendingResult(tailT, makeResult(hn.Tail, HitResult.Miss),       countsAsJudgment: true));
            pending.Add(new PendingResult(tailT, makeResult(hn.Body, HitResult.ComboBreak), countsAsJudgment: false));
            pending.Add(new PendingResult(tailT, makeResult(hn,      HitResult.Miss),       countsAsJudgment: false));
        }
    }

    private static JudgementResult makeResult(HitObject obj, HitResult type)
    {
        var r = new JudgementResult(obj, obj.Judgement);
        r.Type = type;
        return r;
    }

    // ─── .osr parsing ──────────────────────────────────────────────────────────

    private static (List<(double time, int col, bool isPress)> events, string date) ParseReplay(string osrPath)
    {
        byte[] compressedReplay;
        string replayDate = string.Empty;

        using (var fs = File.OpenRead(osrPath))
        using (var br = new BinaryReader(fs))
        {
            br.ReadByte();         // game_mode
            br.ReadInt32();        // version
            ReadOsrString(br);     // beatmap_md5
            ReadOsrString(br);     // player
            ReadOsrString(br);     // replay_md5
            br.ReadInt16();        // c300
            br.ReadInt16();        // c100
            br.ReadInt16();        // c50
            br.ReadInt16();        // cgeki
            br.ReadInt16();        // ckatu
            br.ReadInt16();        // cmiss
            br.ReadInt32();        // score
            br.ReadInt16();        // max_combo
            br.ReadByte();         // perfect
            br.ReadInt32();        // mods
            ReadOsrString(br);     // life bar
            long timestamp = br.ReadInt64();
            try
            {
                replayDate = new DateTime(timestamp, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd");
            }
            catch { /* leave empty */ }
            int len = br.ReadInt32();
            compressedReplay = br.ReadBytes(len);
        }

        string replayText = DecompressLzmaToString(compressedReplay);

        var keyEvents = new List<(double time, int col, bool isPress)>();
        long curTime  = 0;
        int  prevKeys = 0;

        foreach (string frame in replayText.Split(','))
        {
            string f = frame.Trim();
            if (string.IsNullOrEmpty(f)) continue;
            string[] parts = f.Split('|');
            if (parts.Length < 2) continue;
            if (parts[0] == "-12345") continue;

            if (!int.TryParse(parts[0], out int dt))
                dt = (int)Math.Round(float.Parse(parts[0]));
            if (!int.TryParse(parts[1], out int ks)) continue;

            curTime += dt;
            double t = curTime;

            for (int col = 0; col < 18; col++)
            {
                int bit = 1 << col;
                bool was = (prevKeys & bit) != 0;
                bool now = (ks      & bit) != 0;
                if      (!was && now)  keyEvents.Add((t, col, true));
                else if ( was && !now) keyEvents.Add((t, col, false));
            }
            prevKeys = ks;
        }

        keyEvents.Sort((a, b) => a.time.CompareTo(b.time));
        return (keyEvents, replayDate);
    }

    private static string DecompressLzmaToString(byte[] data)
    {
        using var ms = new MemoryStream(data);
        byte[] props = new byte[5];
        ms.Read(props, 0, 5);
        long outSize = 0;
        for (int i = 0; i < 8; i++)
            outSize |= (long)(byte)ms.ReadByte() << (8 * i);
        long compressedSize = ms.Length - ms.Position;

        Assembly? sharpCompress = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "SharpCompress");

        if (sharpCompress == null)
        {
            string? osuDir = Path.GetDirectoryName(typeof(IBeatmap).Assembly.Location);
            if (osuDir != null)
            {
                string candidate = Path.Combine(osuDir, "SharpCompress.dll");
                if (File.Exists(candidate))
                    sharpCompress = Assembly.LoadFrom(candidate);
            }
        }

        var lzmaType = sharpCompress?.GetType("SharpCompress.Compressors.LZMA.LzmaStream");

        var create = lzmaType?.GetMethod("Create",
            new[] { typeof(byte[]), typeof(Stream), typeof(long), typeof(long), typeof(bool) });

        using var lzmaStream = create?.Invoke(null, new object[] { props, ms, compressedSize, outSize, false }) as Stream
            ?? throw new InvalidOperationException("SharpCompress.LzmaStream.Create not found");
        using var sr = new StreamReader(lzmaStream);
        return sr.ReadToEnd();
    }

    private static void ReadOsrString(BinaryReader br)
    {
        if (br.ReadByte() == 0x00) return;
        int length = 0, shift = 0;
        while (true)
        {
            byte b = br.ReadByte();
            length |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        br.ReadBytes(length);
    }
}
