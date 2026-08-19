using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Calculators;
using LazerSR.Hook.Drawables;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

public class StrainGraphWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [SettingSource("Show honey spots", "Overlays honey-spot markers on the strain graph")]
    public BindableBool ShowHoney { get; } = new(true);

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private IGameplayClock? gameplayClock { get; set; }

    private StrainAreaGraph graph = null!;
    private OsuSpriteText? placeholder;
    private CancellationTokenSource? cts;

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

        graph.CurrentTimeProvider = () => gameplayClock?.CurrentTime;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ShowHoney.BindValueChanged(_ => triggerRecalculate());
        triggerRecalculate();
    }

    private void triggerRecalculate()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        var state = gameplayState;
        if (state == null || state.Beatmap is not ManiaBeatmap)
            return;

        var beatmap = state.Beatmap;
        var mods = state.Mods;
        bool showHoney = ShowHoney.Value;

        Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var data = SunnyStrainGraph.Calculate(beatmap, mods, token);
                token.ThrowIfCancellationRequested();

                Schedule(() =>
                {
                    if (token.IsCancellationRequested) return;
                    graph.Update(data, showHoney);
                    graph.Alpha = data.Strain.Length > 0 ? 1f : 0f;
                    if (placeholder != null)
                        placeholder.Alpha = data.Strain.Length > 0 ? 0f : 0.6f;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Trace.WriteLine($"[LazerSR] StrainGraphWidget recalc failed: {e}");
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
