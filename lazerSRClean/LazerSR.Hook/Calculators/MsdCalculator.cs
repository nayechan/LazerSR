using System.Runtime.InteropServices;
using LazerSR.Hook.Data;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;

namespace LazerSR.Hook.Calculators;

// MinaCalc 505 caches a thread_local reference to the Calc object passed on a thread's
// first calc_msd call (Ulbu.h: `Calc& _calc`). Creating/destroying a Calc per call left
// that reference dangling whenever a thread-pool thread was reused — a use-after-free that
// crashed osu! after a few map changes. Fix: one persistent Calc, and all native calls
// confined to a single dedicated worker thread so that cached reference stays valid.
public static class MsdCalculator
{
    private static volatile bool _available;
    private static IntPtr _calc;

    public static volatile SkillsetTimelineData? LastTimeline;

    private static readonly object _mailboxLock = new();
    private static MsdRequest? _pending;
    private static readonly AutoResetEvent _signal = new(false);

    private sealed class MsdRequest(IBeatmap beatmap, HashSet<int>? keepTimesMs = null)
    {
        public readonly IBeatmap Beatmap = beatmap;
        public readonly HashSet<int>? KeepTimesMs = keepTimesMs;
        public readonly TaskCompletionSource<MsdData?> Result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    static MsdCalculator()
    {
        NativeLibrary.SetDllImportResolver(typeof(MsdCalculator).Assembly, (name, _, _) =>
        {
            if (name != "MinaCalc") return IntPtr.Zero;
            string? d = Path.GetDirectoryName(typeof(MsdCalculator).Assembly.Location);
            if (d == null) return IntPtr.Zero;
            return NativeLibrary.TryLoad(Path.Combine(d, "MinaCalc.dll"), out var h) ? h : IntPtr.Zero;
        });

        new Thread(WorkerLoop) { IsBackground = true, Name = "LazerSR-MSD" }.Start();
    }

    public static MsdData? Calculate(IBeatmap beatmap) => Enqueue(beatmap, null);

    public static MsdData? CalculateFiltered(IBeatmap beatmap, HashSet<int> keepTimesMs) =>
        Enqueue(beatmap, keepTimesMs);

    private static MsdData? Enqueue(IBeatmap beatmap, HashSet<int>? keepTimesMs)
    {
        if (beatmap == null) return null;

        var req = new MsdRequest(beatmap, keepTimesMs);
        MsdRequest? superseded;
        lock (_mailboxLock)
        {
            superseded = _pending;
            _pending = req;
        }

        superseded?.Result.TrySetResult(null);
        _signal.Set();
        return req.Result.Task.GetAwaiter().GetResult();
    }

    private static void WorkerLoop()
    {
        try
        {
            _calc = CreateCalc();
            _available = _calc != IntPtr.Zero;
        }
        catch
        {
            _available = false;
        }

        while (true)
        {
            _signal.WaitOne();

            MsdRequest? req;
            lock (_mailboxLock)
            {
                req = _pending;
                _pending = null;
            }
            if (req == null) continue;

            req.Result.TrySetResult(RunCalc(req));
        }
    }

    private static MsdData? RunCalc(MsdRequest req)
    {
        if (!_available) return null;

        try
        {
            var rows = ConvertBeatmap(req.Beatmap, req.KeepTimesMs);
            if (rows == null || rows.Length == 0) return null;

            var result = CalcMsd(_calc, rows, (UIntPtr)rows.Length);
            var msd = new MsdData(
                result.R10.Overall, result.R10.Stream, result.R10.Jumpstream, result.R10.Handstream,
                result.R10.Stamina, result.R10.Jackspeed, result.R10.Chordjack, result.R10.Technical);

            if (req.KeepTimesMs == null)
                LastTimeline = ComputeTimeline(rows);

            return msd;
        }
        catch
        {
            return null;
        }
    }

    private static SkillsetTimelineData? ComputeTimeline(NoteInfoNative[] rows)
    {
        const int maxIntervals = 32768;
        var buf = new float[maxIntervals * 6];
        int n = CalcTimeline(_calc, rows, (UIntPtr)rows.Length, buf, buf.Length);
        if (n <= 0) return null;

        var stream = new float[n]; var js = new float[n]; var hs = new float[n];
        var jack = new float[n]; var cj = new float[n]; var tech = new float[n];
        for (int i = 0; i < n; i++)
        {
            stream[i] = buf[i * 6 + 0]; js[i] = buf[i * 6 + 1]; hs[i] = buf[i * 6 + 2];
            jack[i] = buf[i * 6 + 3]; cj[i] = buf[i * 6 + 4]; tech[i] = buf[i * 6 + 5];
        }
        return new SkillsetTimelineData(stream, js, hs, jack, cj, tech, n);
    }

    private static NoteInfoNative[]? ConvertBeatmap(IBeatmap beatmap, HashSet<int>? keepTimesMs = null)
    {
        if (beatmap is not ManiaBeatmap mb || mb.TotalColumns != 4)
            return null;

        var grouped = new SortedDictionary<int, uint>();
        foreach (var obj in mb.HitObjects)
        {
            if (obj is not ManiaHitObject mho) continue;
            int ms = (int)Math.Round(mho.StartTime);
            if (keepTimesMs != null && !keepTimesMs.Contains(ms)) continue;
            grouped.TryGetValue(ms, out uint existing);
            grouped[ms] = existing | (1u << mho.Column);
        }

        var rows = new NoteInfoNative[grouped.Count];
        int i = 0;
        foreach (var kv in grouped)
            rows[i++] = new NoteInfoNative { Notes = kv.Value, RowTime = kv.Key / 1000f };
        return rows;
    }

    [DllImport("MinaCalc", EntryPoint = "create_calc", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CreateCalc();

    [DllImport("MinaCalc", EntryPoint = "calc_msd", CallingConvention = CallingConvention.Cdecl)]
    private static extern MsdForAllRatesNative CalcMsd(IntPtr calc, NoteInfoNative[] rows, UIntPtr numRows);

    [DllImport("MinaCalc", EntryPoint = "calc_timeline", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CalcTimeline(IntPtr calc, NoteInfoNative[] rows, UIntPtr numRows, float[] outBuf, int bufCapacity);

    [StructLayout(LayoutKind.Sequential)]
    private struct NoteInfoNative { public uint Notes; public float RowTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SsrNative
    {
        public float Overall, Stream, Jumpstream, Handstream, Stamina, Jackspeed, Chordjack, Technical;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsdForAllRatesNative
    {
        public SsrNative R07, R08, R09, R10, R11, R12, R13, R14, R15, R16, R17, R18, R19, R20;
    }
}
