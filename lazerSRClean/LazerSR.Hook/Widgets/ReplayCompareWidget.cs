using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Data;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;

namespace LazerSR.Hook.Widgets;

public class ReplayCompareWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    // ── Settings ──────────────────────────────────────────────
    [SettingSource("투명도 (0-100)")]
    public BindableNumber<float> BackgroundOpacity { get; } =
        new BindableFloat(54) { MinValue = 0, MaxValue = 100, Precision = 1 };

    [SettingSource("값 크기 px")]
    public BindableNumber<float> ValueFontSize { get; } =
        new BindableFloat(15) { MinValue = 8, MaxValue = 24, Precision = 0.5f };

    [SettingSource("Diff 크기 px")]
    public BindableNumber<float> DiffFontSize { get; } =
        new BindableFloat(26) { MinValue = 10, MaxValue = 40, Precision = 0.5f };

    [SettingSource("행 간격 px")]
    public BindableNumber<float> RowSpacing { get; } =
        new BindableFloat(6) { MinValue = -4, MaxValue = 20, Precision = 1 };

    [SettingSource("구분선")]
    public Bindable<bool> ShowDivider { get; } = new BindableBool(true);

    [SettingSource("헤더")]
    public Bindable<bool> ShowHeader { get; } = new BindableBool(true);

    [SettingSource("Score")]
    public Bindable<bool> ShowScore { get; } = new BindableBool(true);

    [SettingSource("Acc")]
    public Bindable<bool> ShowAcc { get; } = new BindableBool(true);

    [SettingSource("PP")]
    public Bindable<bool> ShowPP { get; } = new BindableBool(true);

    [SettingSource("320")]
    public Bindable<bool> Show320 { get; } = new BindableBool(true);

    [SettingSource("300")]
    public Bindable<bool> Show300 { get; } = new BindableBool(true);

    [SettingSource("200")]
    public Bindable<bool> Show200 { get; } = new BindableBool(true);

    [SettingSource("100")]
    public Bindable<bool> Show100 { get; } = new BindableBool(true);

    [SettingSource("50")]
    public Bindable<bool> Show50 { get; } = new BindableBool(true);

    [SettingSource("Miss")]
    public Bindable<bool> ShowMiss { get; } = new BindableBool(true);

    [SettingSource("세부정보 (Live/Replay 수치)")]
    public Bindable<bool> ShowDetails { get; } = new BindableBool(true);

    [SettingSource("글꼴")]
    public Bindable<Typeface> Font { get; } = new Bindable<Typeface>(Typeface.Torus);

    // ── Fixed values from mockup.json ────────────────────────
    private const float WIDGET_WIDTH = 284;
    private const float PADDING = 10;
    private const float HEADER_FONT = 14;
    private static readonly Colour4 COL_HEADER = Colour4.FromHex("#aaaacc");
    private static readonly Colour4 COL_VALUE  = Colour4.FromHex("#eeeeee");
    private static readonly Colour4 COL_POS    = Colour4.FromHex("#4499ff");
    private static readonly Colour4 COL_NEG    = Colour4.FromHex("#ff4444");
    private static readonly Colour4 COL_ZERO   = Colour4.FromHex("#888888");
    private static readonly Colour4 COL_DIV    = Colour4.FromHex("#333355");
    private static readonly Colour4 COL_BORDER = Colour4.FromHex("#444466");

    // ── Dependencies ──────────────────────────────────────────
    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private IGameplayClock? gameplayClock { get; set; }

    // ── PP ────────────────────────────────────────────────────
    private List<TimedDifficultyAttributes>? _timedAttributes;
    private PerformanceCalculator? _performanceCalculator;
    private ScoreInfo? _scoreInfo;
    private readonly CancellationTokenSource _loadCts = new CancellationTokenSource();

    // ── UI ────────────────────────────────────────────────────
    private Box _bg = null!;
    private FillFlowContainer _flow = null!;

    private Container _headerContainer = null!;
    private OsuSpriteText _headerLive  = null!;
    private OsuSpriteText _headerDate  = null!;
    private Box _headerDivider = null!;

    private DataRow _rowScore = null!;
    private DataRow _rowAcc   = null!;
    private DataRow _rowPP    = null!;
    private DataRow _row320   = null!;
    private DataRow _row300   = null!;
    private DataRow _row200   = null!;
    private DataRow _row100   = null!;
    private DataRow _row50    = null!;
    private DataRow _rowMiss  = null!;

    private readonly List<DataRow> _allRows = new();

    public ReplayCompareWidget()
    {
        Width = WIDGET_WIDTH;
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 10;
        BorderColour = COL_BORDER;
        BorderThickness = 1;
    }

    [BackgroundDependencyLoader]
    private void load(BeatmapDifficultyCache difficultyCache)
    {
        float rh = RowHeight;

        _headerContainer = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = HEADER_FONT * 1.8f,
            Children = new Drawable[]
            {
                _headerLive = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font   = OsuFont.Default.With(size: HEADER_FONT, weight: FontWeight.SemiBold),
                    Colour = COL_HEADER,
                    Text   = "Live",
                },
                _headerDate = new OsuSpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Font   = OsuFont.Default.With(size: HEADER_FONT),
                    Colour = COL_HEADER,
                    Alpha  = 0.7f,
                    Text   = "YYYY-MM-DD",
                },
                _headerDivider = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = COL_DIV,
                },
            }
        };

        _rowScore = MakeRow(rh); _rowAcc  = MakeRow(rh); _rowPP   = MakeRow(rh);
        _row320   = MakeRow(rh); _row300  = MakeRow(rh); _row200  = MakeRow(rh);
        _row100   = MakeRow(rh); _row50   = MakeRow(rh); _rowMiss = MakeRow(rh, divider: false);

        _allRows.AddRange(new[] { _rowScore, _rowAcc, _rowPP, _row320, _row300, _row200, _row100, _row50, _rowMiss });

        InternalChildren = new Drawable[]
        {
            _bg = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.Black,
                Alpha  = BackgroundOpacity.Value / 100f,
            },
            _flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes     = Axes.Y,
                Direction        = FillDirection.Vertical,
                Padding          = new MarginPadding(PADDING),
                Children = new Drawable[]
                {
                    _headerContainer,
                    _rowScore.Container, _rowAcc.Container,  _rowPP.Container,
                    _row320.Container,   _row300.Container,  _row200.Container,
                    _row100.Container,   _row50.Container,   _rowMiss.Container,
                }
            }
        };

        // ── PP 초기화 (PerformancePointsCounter와 동일 패턴) ──
        if (gameplayState != null)
        {
            _performanceCalculator = gameplayState.Ruleset.CreatePerformanceCalculator();
            var clonedMods = gameplayState.Mods.Select(m => m.DeepClone()).ToArray();
            _scoreInfo = new ScoreInfo(gameplayState.Score.ScoreInfo.BeatmapInfo, gameplayState.Score.ScoreInfo.Ruleset) { Mods = clonedMods };
            var wb = new GameplayWorkingBeatmap(gameplayState.Beatmap);
            difficultyCache.GetTimedDifficultyAttributesAsync(wb, gameplayState.Ruleset, clonedMods, _loadCts.Token)
                           .ContinueWith(task => Schedule(() => _timedAttributes = task.GetResultSafely()),
                                         TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        BackgroundOpacity.BindValueChanged(e => _bg.Alpha = e.NewValue / 100f, true);
        RowSpacing.BindValueChanged(e => _flow.Spacing = new Vector2(0, e.NewValue), true);

        DiffFontSize.BindValueChanged(_ => { UpdateFonts(); UpdateRowHeights(); }, true);
        ValueFontSize.BindValueChanged(_ => UpdateFonts(), true);

        ShowDivider.BindValueChanged(e =>
        {
            _headerDivider.Alpha = e.NewValue && ShowHeader.Value ? 1 : 0;
            foreach (var r in _allRows) r.UpdateDivider(e.NewValue);
        }, true);

        ShowHeader.BindValueChanged(e =>
        {
            _headerContainer.Alpha = e.NewValue ? 1 : 0;
            _headerDivider.Alpha   = e.NewValue && ShowDivider.Value ? 1 : 0;
        }, true);

        BindRowVisibility(ShowScore, _rowScore);
        BindRowVisibility(ShowAcc,   _rowAcc);
        BindRowVisibility(ShowPP,    _rowPP);
        BindRowVisibility(Show320,   _row320);
        BindRowVisibility(Show300,   _row300);
        BindRowVisibility(Show200,   _row200);
        BindRowVisibility(Show100,   _row100);
        BindRowVisibility(Show50,    _row50);
        BindRowVisibility(ShowMiss,  _rowMiss);

        ShowDetails.BindValueChanged(e =>
        {
            float liveAlpha   = e.NewValue ? 1f : 0f;
            float replayAlpha = e.NewValue ? 0.7f : 0f;
            foreach (var r in _allRows)
            {
                r.LiveText.Alpha   = liveAlpha;
                r.ReplayText.Alpha = replayAlpha;
            }
        }, true);

        Font.BindValueChanged(_ => UpdateFonts(), true);

        if (gameplayState != null)
            Scheduler.AddDelayed(UpdateDisplay, 100, true);
        else
            ShowPlaceholder();
    }

    private void BindRowVisibility(Bindable<bool> setting, DataRow row)
        => setting.BindValueChanged(e => row.Container.Alpha = e.NewValue ? 1f : 0f, true);

    private float RowHeight => DiffFontSize.Value * 1.4f;

    private void UpdateRowHeights()
    {
        float rh = RowHeight;
        foreach (var r in _allRows) r.Container.Height = rh;
    }

    private void UpdateFonts()
    {
        var vf = OsuFont.GetFont(Font.Value, size: ValueFontSize.Value);
        var df = OsuFont.GetFont(Font.Value, size: DiffFontSize.Value, weight: FontWeight.Bold);
        foreach (var r in _allRows)
        {
            r.LiveText.Font   = vf;
            r.DiffText.Font   = df;
            r.ReplayText.Font = vf;
        }
    }

    private void ShowPlaceholder()
    {
        _headerDate.Text = "YYYY-MM-DD";
        foreach (var r in _allRows)
        {
            r.LiveText.Text   = "—";
            r.DiffText.Text   = "±0";
            r.DiffText.Colour = COL_ZERO;
            r.ReplayText.Text = "—";
        }
    }

    private void UpdateDisplay()
    {
        if (gameplayState == null) return;

        var sp = gameplayState.ScoreProcessor;
        long liveScore = sp.TotalScore.Value;
        var  stats     = sp.Statistics;

        int liveJ320 = GetStat(stats, HitResult.Perfect);
        int liveJ300 = GetStat(stats, HitResult.Great);
        int liveJ200 = GetStat(stats, HitResult.Good);
        int liveJ100 = GetStat(stats, HitResult.Ok);
        int liveJ50  = GetStat(stats, HitResult.Meh);
        int liveMiss = GetStat(stats, HitResult.Miss);

        _headerDate.Text = ReplayCompareState.ReplayDate.Length > 0
            ? ReplayCompareState.ReplayDate : "—";

        var tl = ReplayCompareState.Timeline;
        double curTime = gameplayClock?.CurrentTime ?? Clock.CurrentTime;
        if (tl == null || tl.Length == 0 || curTime < tl[0].Time)
        {
            // replay not ready yet — show live values with "—" for replay side
            double liveAccOnly = CalcAcc(liveJ320, liveJ300, liveJ200, liveJ100, liveJ50, liveMiss);
            var attrib0 = GetAttributeAtTime(curTime);
            string livePPStr0 = FormatPP(_performanceCalculator, _scoreInfo, attrib0,
                liveJ320, liveJ300, liveJ200, liveJ100, liveJ50, liveMiss);
            ApplyRow(_rowScore, liveScore.ToString("N0"),           "—", COL_ZERO, "—");
            ApplyRow(_rowAcc,   liveAccOnly.ToString("F2") + "%",   "—", COL_ZERO, "—");
            ApplyRow(_rowPP,    livePPStr0,                         "—", COL_ZERO, "—");
            ApplyRow(_row320,   liveJ320.ToString(),          "—", COL_ZERO, "—");
            ApplyRow(_row300,   liveJ300.ToString(),          "—", COL_ZERO, "—");
            ApplyRow(_row200,   liveJ200.ToString(),          "—", COL_ZERO, "—");
            ApplyRow(_row100,   liveJ100.ToString(),          "—", COL_ZERO, "—");
            ApplyRow(_row50,    liveJ50.ToString(),           "—", COL_ZERO, "—");
            ApplyRow(_rowMiss,  liveMiss.ToString(),          "—", COL_ZERO, "—");
            return;
        }

        int idx = TimelineSearch(tl, curTime);
        var frame = tl[idx];

        double liveAcc   = CalcAcc(liveJ320, liveJ300, liveJ200, liveJ100, liveJ50, liveMiss);
        double replayAcc = CalcAcc(frame.J320, frame.J300, frame.J200, frame.J100, frame.J50, frame.JMiss);
        double accDiff   = liveAcc - replayAcc;

        // rows 2-5: neg=red, pos=blue
        SetDataRow(_rowScore, liveScore,   frame.Score, liveScore.ToString("N0"),   frame.Score.ToString("N0"), false);
        ApplyRow(_rowAcc,
            liveAcc.ToString("F2") + "%",
            (accDiff >= 0 ? "+" : "") + accDiff.ToString("F2") + "%",
            accDiff == 0 ? COL_ZERO : accDiff > 0 ? COL_POS : COL_NEG,
            replayAcc.ToString("F2") + "%");
        var attrib = GetAttributeAtTime(curTime);
        if (attrib != null && _performanceCalculator != null && _scoreInfo != null)
        {
            _scoreInfo.Statistics = BuildStats(liveJ320, liveJ300, liveJ200, liveJ100, liveJ50, liveMiss);
            double livePP   = _performanceCalculator.Calculate(_scoreInfo, attrib).Total;
            _scoreInfo.Statistics = BuildStats(frame.J320, frame.J300, frame.J200, frame.J100, frame.J50, frame.JMiss);
            double replayPP = _performanceCalculator.Calculate(_scoreInfo, attrib).Total;
            double ppDiff   = livePP - replayPP;
            ApplyRow(_rowPP,
                ((int)Math.Round(livePP))   + "pp",
                (ppDiff >= 0 ? "+" : "") + ((int)Math.Round(ppDiff)) + "pp",
                ppDiff == 0 ? COL_ZERO : ppDiff > 0 ? COL_POS : COL_NEG,
                ((int)Math.Round(replayPP)) + "pp");
        }
        else
        {
            ApplyRow(_rowPP, "—", "—", COL_ZERO, "—");
        }
        SetDataRow(_row320, liveJ320, frame.J320, liveJ320.ToString(), frame.J320.ToString(), false);

        // rows 6-10: inverted — neg=blue, pos=red
        SetDataRow(_row300, liveJ300, frame.J300, liveJ300.ToString(), frame.J300.ToString(), true);
        SetDataRow(_row200, liveJ200, frame.J200, liveJ200.ToString(), frame.J200.ToString(), true);
        SetDataRow(_row100, liveJ100, frame.J100, liveJ100.ToString(), frame.J100.ToString(), true);
        SetDataRow(_row50,  liveJ50,  frame.J50,  liveJ50.ToString(),  frame.J50.ToString(),  true);
        SetDataRow(_rowMiss, liveMiss, frame.JMiss, liveMiss.ToString(), frame.JMiss.ToString(), true);
    }

    private void SetDataRow(DataRow row, long liveVal, long replayVal,
                            string liveStr, string replayStr, bool inverted)
    {
        long diff   = liveVal - replayVal;
        string dStr = FormatDiff(diff);
        Colour4 col = diff == 0 ? COL_ZERO
                    : inverted  ? (diff > 0 ? COL_NEG : COL_POS)
                                : (diff > 0 ? COL_POS : COL_NEG);
        ApplyRow(row, liveStr, dStr, col, replayStr);
    }

    private static void ApplyRow(DataRow row, string live, string diff, Colour4 diffCol, string replay)
    {
        row.LiveText.Text   = live;
        row.DiffText.Text   = diff;
        row.DiffText.Colour = diffCol;
        row.ReplayText.Text = replay;
    }

    private static string FormatDiff(long diff)
    {
        if (diff == 0) return "±0";
        return (diff > 0 ? "+" : "") + diff.ToString("N0");
    }

    private static double CalcAcc(int j320, int j300, int j200, int j100, int j50, int jMiss)
    {
        int total = j320 + j300 + j200 + j100 + j50 + jMiss;
        if (total == 0) return 100.0;
        double num = 305.0 * j320 + 300.0 * j300 + 200.0 * j200 + 100.0 * j100 + 50.0 * j50;
        return num / (305.0 * total) * 100.0;
    }

    private DifficultyAttributes? GetAttributeAtTime(double time)
    {
        if (_timedAttributes == null || _timedAttributes.Count == 0) return null;
        int idx = _timedAttributes.BinarySearch(new TimedDifficultyAttributes(time, null));
        if (idx < 0) idx = ~idx - 1;
        return _timedAttributes[Math.Clamp(idx, 0, _timedAttributes.Count - 1)].Attributes;
    }

    private static Dictionary<HitResult, int> BuildStats(int j320, int j300, int j200, int j100, int j50, int jMiss)
        => new()
        {
            [HitResult.Perfect] = j320,
            [HitResult.Great]   = j300,
            [HitResult.Good]    = j200,
            [HitResult.Ok]      = j100,
            [HitResult.Meh]     = j50,
            [HitResult.Miss]    = jMiss,
        };

    private string FormatPP(PerformanceCalculator? calc, ScoreInfo? si, DifficultyAttributes? attr,
        int j320, int j300, int j200, int j100, int j50, int jMiss)
    {
        if (calc == null || si == null || attr == null) return "—";
        si.Statistics = BuildStats(j320, j300, j200, j100, j50, jMiss);
        return ((int)Math.Round(calc.Calculate(si, attr).Total)) + "pp";
    }

    private static int GetStat(IReadOnlyDictionary<HitResult, int> stats, HitResult key)
        => stats.TryGetValue(key, out int v) ? v : 0;

    private static int TimelineSearch(ReplayFrame[] tl, double t)
    {
        int lo = 0, hi = tl.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (tl[mid].Time <= t) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private static DataRow MakeRow(float height, bool divider = true)
    {
        var row = new DataRow(height, divider);
        return row;
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        _loadCts.Cancel();
        Scheduler.CancelDelayedTasks();
    }

    // ── nested row ────────────────────────────────────────────
    private sealed class DataRow
    {
        public readonly Container     Container;
        public readonly OsuSpriteText LiveText;
        public readonly OsuSpriteText DiffText;
        public readonly OsuSpriteText ReplayText;
        private readonly Box?          _dividerBox;

        public DataRow(float height, bool hasDivider)
        {
            LiveText = new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Font   = OsuFont.Default.With(size: 15),
                Colour = COL_VALUE,
            };
            DiffText = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font   = OsuFont.Default.With(size: 26, weight: FontWeight.Bold),
                Colour = COL_ZERO,
            };
            ReplayText = new OsuSpriteText
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Font   = OsuFont.Default.With(size: 15),
                Colour = COL_VALUE,
                Alpha  = 0.7f,
            };

            var children = new List<Drawable> { LiveText, DiffText, ReplayText };

            if (hasDivider)
            {
                _dividerBox = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = COL_DIV,
                };
                children.Add(_dividerBox);
            }

            Container = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height           = height,
                Children         = children,
            };
        }

        public void UpdateDivider(bool show)
        {
            if (_dividerBox != null) _dividerBox.Alpha = show ? 1 : 0;
        }
    }

    // Copied from PerformancePointsCounter — wraps IBeatmap as WorkingBeatmap for BeatmapDifficultyCache
    private class GameplayWorkingBeatmap : WorkingBeatmap
    {
        private readonly IBeatmap gameplayBeatmap;

        public GameplayWorkingBeatmap(IBeatmap gameplayBeatmap)
            : base(gameplayBeatmap.BeatmapInfo, null)
        {
            this.gameplayBeatmap = gameplayBeatmap;
        }

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
            => gameplayBeatmap;

        protected override IBeatmap GetBeatmap() => gameplayBeatmap;

        public override Texture GetBackground() => throw new NotImplementedException();
        protected override Track GetBeatmapTrack() => throw new NotImplementedException();
        protected override ISkin GetSkin() => throw new NotImplementedException();
        public override Stream GetStream(string storagePath) => throw new NotImplementedException();
    }
}
