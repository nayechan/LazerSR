using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Calculators;
using LazerSR.Hook.Drawables;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

public class MsdSkillsetWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private IBindable<RulesetInfo>? ruleset { get; set; }

    private MsdBarChart chart = null!;
    private OsuSpriteText? placeholder;
    private CancellationTokenSource? cts;

    public MsdSkillsetWidget()
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
            chart = new MsdBarChart
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0f,
            },
            placeholder = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "MSD Skillsets",
                Font = OsuFont.Default.With(size: 12f, weight: FontWeight.SemiBold),
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
            workingBeatmap?.BindValueChanged(_ => triggerRecalculate());
            ruleset?.BindValueChanged(_ => triggerRecalculate());
            triggerRecalculate();
        }
    }

    private void triggerRecalculate()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        var gs = gameplayState;
        var wb = workingBeatmap?.Value;
        IRulesetInfo? rs = ruleset?.Value;

        Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();

                IBeatmap playable;
                if (gs != null)
                {
                    if (gs.Beatmap is not ManiaBeatmap mb || mb.TotalColumns != 4) return;
                    playable = gs.Beatmap;
                }
                else
                {
                    if (wb == null || wb.BeatmapInfo.Ruleset.ShortName != "mania") return;
                    playable = wb.GetPlayableBeatmap(rs ?? wb.BeatmapInfo.Ruleset, Array.Empty<Mod>(), token);
                }

                token.ThrowIfCancellationRequested();
                var msd = MsdCalculator.Calculate(playable);
                token.ThrowIfCancellationRequested();

                if (msd == null) return;

                Schedule(() =>
                {
                    if (token.IsCancellationRequested) return;
                    chart.SetData(msd);
                    chart.Alpha = 1f;
                    if (placeholder != null) placeholder.Alpha = 0f;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] MsdSkillsetWidget recalc failed: {e}");
            }
        }, token);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        cts?.Cancel();
        cts?.Dispose();
    }
}
