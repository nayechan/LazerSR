using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Calculators;
using LazerSR.Hook.Data;
using LazerSR.Hook.Drawables;
using LazerSR.SunnyCalculator.Tuning;
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
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

public class StrainGraphWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [SettingSource("PP Honey Spot 임계값 (%)")]
    public BindableNumber<float> HoneySpot2Threshold { get; } =
        new BindableFloat(40) { MinValue = 1, MaxValue = 100, Precision = 1 };

    [SettingSource("PP 가중치 지수")]
    public BindableNumber<float> PPWeightExponent { get; } =
        new BindableFloat(1.5f) { MinValue = 0, MaxValue = 4, Precision = 0.1f };

    [SettingSource("개인화 오버레이 표시", "빨강/파랑으로 만인 대비 개인화 강도 차이를 표시합니다")]
    public BindableBool ShowPersonalOverlay { get; } = new BindableBool(false);

    // gameplay context
    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private IGameplayClock? gameplayClock { get; set; }

    // song select context
    [Resolved(canBeNull: true)]
    private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private IBindable<RulesetInfo>? ruleset { get; set; }

    [Resolved(canBeNull: true)]
    private IBindable<IReadOnlyList<Mod>>? mods { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapDifficultyCache? difficultyCache { get; set; }

    private StrainAreaGraph graph = null!;
    private OsuSpriteText? placeholder;
    private CancellationTokenSource? cts;
    private CancellationTokenSource? _debounceCts;
    private ModSettingChangeTracker? _modTracker;

    public StrainGraphWidget()
    {
        Width = 320;
        Height = 80;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.Black,
                Alpha = 0.55f,
            },
            graph = new StrainAreaGraph
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0f,
            },
            placeholder = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "Sunny Strain Graph",
                Font = OsuFont.Default.With(size: 12f, weight: FontWeight.SemiBold),
                Alpha = 0.6f,
            },
        };

        graph.CurrentTimeProvider = () =>
        {
            if (gameplayClock != null) return gameplayClock.CurrentTime;
            var wb = workingBeatmap?.Value;
            return wb?.TrackLoaded == true ? wb.Track.CurrentTime : (double?)null;
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        HoneySpot2Threshold.BindValueChanged(_ => scheduleRecalculate());
        PPWeightExponent.BindValueChanged(_ => scheduleRecalculate());
        ShowPersonalOverlay.BindValueChanged(_ => scheduleRecalculate());

        if (gameplayState != null)
        {
            triggerRecalculate();
        }
        else
        {
            workingBeatmap?.BindValueChanged(_ => scheduleRecalculate());
            ruleset?.BindValueChanged(_ => scheduleRecalculate());
            mods?.BindValueChanged(e =>
            {
                _modTracker?.Dispose();
                _modTracker = new ModSettingChangeTracker(e.NewValue);
                _modTracker.SettingChanged += _ => scheduleRecalculate();
                scheduleRecalculate();
            }, true);
        }
    }

    private void scheduleRecalculate()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var debounceToken = _debounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, debounceToken);
                triggerRecalculate();
            }
            catch (OperationCanceledException) { }
        });
    }

    private void triggerRecalculate()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        float h2Threshold = HoneySpot2Threshold.Value;
        float ppWeight    = PPWeightExponent.Value;
        bool showPersonal = ShowPersonalOverlay.Value;

        // capture all game-thread values before going to background
        var gs    = gameplayState;
        var wb    = workingBeatmap?.Value;
        IRulesetInfo? rs = ruleset?.Value;
        IReadOnlyList<Mod> modsArr = mods?.Value ?? Array.Empty<Mod>();
        var cache = difficultyCache;

        Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();

                IBeatmap playable;
                IReadOnlyList<Mod> modsToUse;
                IWorkingBeatmap? workingBm;

                if (gs != null)
                {
                    if (gs.Beatmap is not ManiaBeatmap) return;
                    playable   = gs.Beatmap;
                    modsToUse  = gs.Mods;
                    workingBm  = new GameplayWorkingBeatmap(gs.Beatmap);
                }
                else
                {
                    if (wb == null || wb.BeatmapInfo.Ruleset.ShortName != "mania") return;
                    modsToUse = modsArr;
                    playable  = wb.GetPlayableBeatmap(rs ?? wb.BeatmapInfo.Ruleset, modsToUse, token);
                    workingBm = wb;
                }

                token.ThrowIfCancellationRequested();

                // 만인(전역 기본값)과 만인+개인을 같은 비트맵/모드로 한 번씩 - 시간축은 노트 시각에서만
                // 나오므로(상수와 무관) 둘이 항상 일치한다.
                var universalData = SunnyStrainGraph.Calculate(playable, modsToUse, token);
                token.ThrowIfCancellationRequested();

                // 오버레이가 꺼져 있으면 개인화 재계산 자체를 건너뛴다 - universalData를 그대로 써서
                // strongerCaps/weakerCaps 구간이 항상 비게(u == p) 만든다.
                StrainGraphData personalData;
                if (showPersonal)
                {
                    double[] personalDeltas = PersonalDiff.CombinedWithUniversal();
                    personalData = SunnyConstants.WithIsolatedDiff(personalDeltas,
                        () => SunnyStrainGraph.Calculate(playable, modsToUse, token));
                    token.ThrowIfCancellationRequested();
                }
                else
                {
                    personalData = universalData;
                }

                // PP-curve honey spot 2
                double[] honeySpots2 = [];
                try
                {
                    var rulesetInfo = gs != null
                        ? gs.Ruleset.RulesetInfo
                        : wb?.BeatmapInfo.Ruleset;
                    var rulesetInstance = gs != null
                        ? gs.Ruleset
                        : (rulesetInfo as RulesetInfo)?.CreateInstance();

                    if (cache != null && rulesetInstance != null && rulesetInfo != null)
                    {
                        var timedAttrs = await cache.GetTimedDifficultyAttributesAsync(
                            workingBm, rulesetInstance, modsToUse.ToArray(), token);
                        token.ThrowIfCancellationRequested();

                        if (timedAttrs != null && timedAttrs.Count >= 4)
                        {
                            var sampled = timedAttrs
                                .GroupBy(a => (int)(a.Time / 400.0))
                                .Select(g => g.Last())
                                .ToList();
                            var calc = rulesetInstance.CreatePerformanceCalculator();
                            if (calc != null && sampled.Count >= 4)
                                honeySpots2 = PPCurveAnalyzer.Analyze(sampled, calc, h2Threshold, ppWeight, token);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    HookLog.Write($"[LazerSR] honey2 failed: {e}");
                }

                token.ThrowIfCancellationRequested();

                Schedule(() =>
                {
                    if (token.IsCancellationRequested) return;
                    graph.Update(universalData, personalData, honeySpots2);
                    graph.Alpha = universalData.Strain.Length > 0 ? 1f : 0f;
                    if (placeholder != null)
                        placeholder.Alpha = universalData.Strain.Length > 0 ? 0f : 0.6f;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] StrainGraphWidget recalc failed: {e}");
            }
        }, token);
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

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        cts?.Cancel();
        cts?.Dispose();
        _modTracker?.Dispose();
    }
}
