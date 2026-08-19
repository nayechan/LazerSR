using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Calculators;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

public class DanInfoWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private IBindable<RulesetInfo>? ruleset { get; set; }

    [Resolved(canBeNull: true)]
    private IBindable<IReadOnlyList<Mod>>? mods { get; set; }

    // Local bindable so osu! framework auto-unbinds on dispose (prevents stale static callbacks)
    private readonly Bindable<string> _dominant = new();

    private OsuSpriteText text = null!;
    private CancellationTokenSource? cts;
    private ModSettingChangeTracker? _modTracker;

    // Cached raw results from last full computation (no rate applied)
    private int _rawBpm;
    private string _danText = string.Empty;       // e.g. "10th mid"
    private string _dominantAbbr = string.Empty;  // e.g. "CJ"

    public DanInfoWidget()
    {
        Width = 240;
        Height = 28;
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
            text = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = OsuFont.Default.With(size: 14f, weight: FontWeight.SemiBold),
                Text = "Dan Info",
                Alpha = 0.6f,
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (gameplayState != null)
        {
            triggerRecalculate();
        }
        else
        {
            _dominant.BindTo(SunnyState.CurrentDominant);
            _dominant.BindValueChanged(_ => triggerRecalculate(), true);

            mods?.BindValueChanged(e =>
            {
                _modTracker?.Dispose();
                _modTracker = new ModSettingChangeTracker(e.NewValue);
                _modTracker.SettingChanged += _ => updateDisplay();
                updateDisplay();
            }, true);
        }
    }

    // Runs on update thread — just multiplies cached raw BPM by current rate
    private void updateDisplay()
    {
        if (string.IsNullOrEmpty(_danText))
        {
            text.Text = "N/A";
            return;
        }

        double rate = GetRate(mods?.Value);
        int bpm = _rawBpm > 0 ? (int)Math.Round(_rawBpm * rate) : 0;
        string bpmStr = bpm > 0 ? $"{bpm}BPM" : "?BPM";
        text.Text = $"{_danText} {bpmStr} {_dominantAbbr}";
        text.Alpha = 1f;
    }

    private static double GetRate(IReadOnlyList<Mod>? mods)
    {
        if (mods == null) return 1.0;
        double rate = 1.0;
        foreach (var mod in mods)
            if (mod is IApplicableToRate r)
                rate = r.ApplyToRate(0, rate);
        return rate;
    }

    private void triggerRecalculate()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        string dominantAbbr = SunnyState.CurrentDominant.Value;

        // Dominant is empty while DifficultyDisplayPatch is computing — show N/A immediately
        if (string.IsNullOrEmpty(dominantAbbr))
        {
            _danText = string.Empty;
            Schedule(() => text.Text = "N/A");
            return;
        }

        var gs = gameplayState;
        var wb = workingBeatmap?.Value;
        var rs = ruleset?.Value;
        double sr = SunnyState.CurrentSr.Value.Stars;

        Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();

                IBeatmap playable;
                if (gs != null)
                    playable = gs.Beatmap;
                else
                {
                    if (wb == null) return;
                    playable = wb.GetPlayableBeatmap(rs ?? wb.BeatmapInfo.Ruleset, Array.Empty<Mod>(), token);
                }

                token.ThrowIfCancellationRequested();

                string danText = string.Empty;
                int rawBpm = 0;
                string abbr = string.Empty;

                if (playable is ManiaBeatmap && !string.IsNullOrEmpty(dominantAbbr) && dominantAbbr != "N/A")
                {
                    var dan = DanCalculator.GetDanInfo(sr);
                    danText = $"{dan.Name} {dan.Tier}";
                    rawBpm = PatternBpmCalculator.GetMaxBpm(playable, dominantAbbr);
                    abbr = dominantAbbr;
                }

                Schedule(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _danText = danText;
                    _rawBpm = rawBpm;
                    _dominantAbbr = abbr;
                    updateDisplay();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] DanInfoWidget recalc failed: {e}");
            }
        }, token);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        cts?.Cancel();
        cts?.Dispose();
        _modTracker?.Dispose();
    }
}
