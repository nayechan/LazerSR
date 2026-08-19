using System;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Calculators;
using LazerSR.Hook.Data;
using LazerSR.SunnyCalculator;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

public class SectionTimerWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [SettingSource("투명도 (0-100)")]
    public BindableNumber<float> BackgroundOpacity { get; } =
        new BindableFloat(54) { MinValue = 0, MaxValue = 100, Precision = 1 };

    [SettingSource("텍스트 크기 px")]
    public BindableNumber<float> FontSize { get; } =
        new BindableFloat(28) { MinValue = 12, MaxValue = 60, Precision = 1 };

    [SettingSource("레이블 표시")]
    public Bindable<bool> ShowLabel { get; } = new BindableBool(true);

    [SettingSource("어려움 임계값 (%)")]
    public BindableNumber<float> HardThresholdPct { get; } =
        new BindableFloat(75) { MinValue = 1, MaxValue = 99, Precision = 1 };

    [SettingSource("구간 병합 기준 (ms)")]
    public BindableNumber<float> MinSegmentMs { get; } =
        new BindableFloat(4000) { MinValue = 100, MaxValue = 20000, Precision = 100 };

    private static readonly Colour4 COL_HARD = Colour4.Red;
    private static readonly Colour4 COL_REST = Colour4.White;
    private static readonly Colour4 COL_NONE = Colour4.FromHex("#888888");

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private IGameplayClock? gameplayClock { get; set; }

    private Box _bg = null!;
    private OsuSpriteText _label = null!;
    private OsuSpriteText _timeText = null!;

    private SectionInterval[]? _sections;
    private CancellationTokenSource? _cts;

    public SectionTimerWidget()
    {
        Width = 120;
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 8;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            _bg = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.Black,
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding(8),
                Children = new Drawable[]
                {
                    _label = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.Default.With(size: 12, weight: FontWeight.SemiBold),
                        Colour = COL_NONE,
                        Text = "SECTION",
                    },
                    _timeText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.Default.With(size: 28, weight: FontWeight.Bold),
                        Colour = COL_NONE,
                        Text = "—",
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        BackgroundOpacity.BindValueChanged(e => _bg.Alpha = e.NewValue / 100f, true);
        FontSize.BindValueChanged(e =>
            _timeText.Font = OsuFont.Default.With(size: e.NewValue, weight: FontWeight.Bold), true);
        ShowLabel.BindValueChanged(e => _label.Alpha = e.NewValue ? 1f : 0f, true);

        if (gameplayState != null)
        {
            HardThresholdPct.BindValueChanged(_ => TriggerCalculate());
            MinSegmentMs.BindValueChanged(_ => TriggerCalculate());
            TriggerCalculate();
            Scheduler.AddDelayed(UpdateDisplay, 100, true);
        }
    }

    private void TriggerCalculate()
    {
        if (gameplayState?.Beatmap is not ManiaBeatmap) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var beatmap = gameplayState.Beatmap;
        var mods = gameplayState.Mods;
        double threshold = HardThresholdPct.Value / 100.0;
        double minMs = MinSegmentMs.Value;

        Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var raw = new SunnyManiaDifficultyCalculator().GetStrainTimeline(beatmap, mods, token);
                token.ThrowIfCancellationRequested();
                var sections = SectionTimeline.Calculate(raw, threshold, minMs, token);
                if (!token.IsCancellationRequested)
                    Schedule(() => _sections = sections);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] SectionTimerWidget recalc failed: {e}");
            }
        }, token);
    }

    private void UpdateDisplay()
    {
        if (_sections == null || _sections.Length == 0)
        {
            _label.Text = "—";
            _timeText.Text = "—";
            _timeText.Colour = COL_NONE;
            return;
        }

        double curTime = gameplayClock?.CurrentTime ?? Clock.CurrentTime;
        int idx = FindSection(_sections, curTime);

        if (idx < 0)
        {
            _timeText.Text = "—";
            _timeText.Colour = COL_NONE;
            return;
        }

        var section = _sections[idx];
        double remainingMs = Math.Max(0, section.EndMs - curTime);

        _label.Text = section.IsHard ? "HARD" : "REST";
        _label.Colour = section.IsHard ? COL_HARD : COL_REST;
        _timeText.Text = (remainingMs / 1000.0).ToString("F1");
        _timeText.Colour = section.IsHard ? COL_HARD : COL_REST;
    }

    private static int FindSection(SectionInterval[] sections, double time)
    {
        if (time < sections[0].StartMs) return -1;
        int lo = 0, hi = sections.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (sections[mid].StartMs <= time) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        _cts?.Cancel();
        _cts?.Dispose();
        Scheduler.CancelDelayedTasks();
    }
}
