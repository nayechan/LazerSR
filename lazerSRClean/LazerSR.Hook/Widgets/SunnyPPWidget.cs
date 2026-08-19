using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook;
using LazerSR.SunnyCalculator;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

public class SunnyPPWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [SettingSource("투명도 (0-100)")]
    public BindableNumber<float> BackgroundOpacity { get; } =
        new BindableFloat(54) { MinValue = 0, MaxValue = 100, Precision = 1 };

    [SettingSource("텍스트 크기 px")]
    public BindableNumber<float> FontSize { get; } =
        new BindableFloat(20) { MinValue = 8, MaxValue = 40, Precision = 1 };

    [SettingSource("글꼴")]
    public Bindable<Typeface> Font { get; } = new Bindable<Typeface>(Typeface.Torus);

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private IGameplayClock? gameplayClock { get; set; }

    private List<TimedDifficultyAttributes>? _timedAttributes;
    private PerformanceCalculator? _performanceCalculator;
    private ScoreInfo? _scoreInfo;
    private readonly CancellationTokenSource _loadCts = new();

    private Box _bg = null!;
    private OsuSpriteText _ppText = null!;

    public SunnyPPWidget()
    {
        Width = 110;
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 8;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            _bg = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding(8),
                Child = _ppText = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = OsuFont.GetFont(Typeface.Torus, size: 20, weight: FontWeight.Bold),
                    Text = "—",
                },
            },
        };

        if (gameplayState?.Beatmap is ManiaBeatmap)
        {
            _performanceCalculator = gameplayState.Ruleset.CreatePerformanceCalculator();
            var clonedMods = gameplayState.Mods.Select(m => m.DeepClone()).ToArray();
            _scoreInfo = new ScoreInfo(gameplayState.Score.ScoreInfo.BeatmapInfo, gameplayState.Score.ScoreInfo.Ruleset)
            {
                Mods = clonedMods
            };
            var wb = new GameplayWorkingBeatmap(gameplayState.Beatmap);
            var token = _loadCts.Token;

            Task.Run(() =>
            {
                try
                {
                    var attribs = SunnyRunner.CalculateTimed(wb, gameplayState.Ruleset.RulesetInfo, clonedMods, token);
                    if (!token.IsCancellationRequested)
                        Schedule(() => _timedAttributes = attribs);
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { HookLog.Write($"[LazerSR] SunnyPPWidget load failed: {e}"); }
            }, token);
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        BackgroundOpacity.BindValueChanged(e => _bg.Alpha = e.NewValue / 100f, true);
        Font.BindValueChanged(_ => UpdateFont(), true);
        FontSize.BindValueChanged(_ => UpdateFont());

        if (gameplayState != null)
            Scheduler.AddDelayed(UpdateDisplay, 100, true);
    }

    private void UpdateFont()
    {
        _ppText.Font = OsuFont.GetFont(Font.Value, size: FontSize.Value, weight: FontWeight.Bold);
    }

    private void UpdateDisplay()
    {
        if (gameplayState == null || _timedAttributes == null || _performanceCalculator == null || _scoreInfo == null)
            return;

        double curTime = gameplayClock?.CurrentTime ?? Clock.CurrentTime;
        var attrib = GetAttributeAtTime(curTime);
        if (attrib == null) return;

        gameplayState.ScoreProcessor.PopulateScore(_scoreInfo);
        int pp = (int)Math.Round(_performanceCalculator.Calculate(_scoreInfo, attrib).Total);
        _ppText.Text = $"{pp}pp";

        SunnyPPState.ScoreId = gameplayState.Score.ScoreInfo.ID;
        SunnyPPState.Pp = pp;
    }

    private DifficultyAttributes? GetAttributeAtTime(double time)
    {
        if (_timedAttributes == null || _timedAttributes.Count == 0) return null;
        int idx = _timedAttributes.BinarySearch(new TimedDifficultyAttributes(time, null));
        if (idx < 0) idx = ~idx - 1;
        return _timedAttributes[Math.Clamp(idx, 0, _timedAttributes.Count - 1)].Attributes;
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        _loadCts.Cancel();
        Scheduler.CancelDelayedTasks();
    }

    private class GameplayWorkingBeatmap : WorkingBeatmap
    {
        private readonly IBeatmap _beatmap;

        public GameplayWorkingBeatmap(IBeatmap beatmap) : base(beatmap.BeatmapInfo, null)
            => _beatmap = beatmap;

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken ct)
            => _beatmap;

        protected override IBeatmap GetBeatmap() => _beatmap;
        public override Texture GetBackground() => throw new NotImplementedException();
        protected override Track GetBeatmapTrack() => throw new NotImplementedException();
        protected override ISkin GetSkin() => throw new NotImplementedException();
        public override Stream GetStream(string storagePath) => throw new NotImplementedException();
    }
}
