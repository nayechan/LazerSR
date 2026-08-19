using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LazerSR.Hook.Calculators;
using LazerSR.Hook.Data;
using LazerSR.Hook.Drawables;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mods;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Patches;

[HarmonyPatch]
public static class MetadataWedgePatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Screens.Select.BeatmapMetadataWedge";
    private static readonly ConditionalWeakTable<object, MetadataState> _states = new();
    private static readonly MethodInfo? _scheduleMethod =
        AccessTools.Method(typeof(Drawable), "Schedule", new[] { typeof(Action) });
    private static readonly MethodInfo? _setInternalChildMethod =
        AccessTools.PropertySetter(typeof(CompositeDrawable), "InternalChild");

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
        return type == null ? null : AccessTools.Method(type, "LoadComplete");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            if (__instance is not Drawable owner)
                return;

            Type type = __instance.GetType();

            if (!AccessHelper.TryGet<IBindable<WorkingBeatmap>>(type, "beatmap", __instance, out var beatmap) || beatmap == null)
                return;

            // Create strain wedge
            var strainGraph = new StrainAreaGraph
            {
                RelativeSizeAxes = Axes.X,
                Height = 65f,
            };
            strainGraph.CurrentTimeProvider = () =>
            {
                try { return beatmap.Value?.Track?.CurrentTime; }
                catch { return null; }
            };

            var strainWedge = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                CornerRadius = 10,
                Masking = true,
                Alpha = 0f,
                Children = new Drawable[]
                {
                    new WedgeBackground(),
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Shear = -OsuGame.SHEAR,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0f, 4f),
                        Padding = new MarginPadding { Horizontal = 16f, Vertical = 12f },
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = "Sunny Difficulty Graph",
                                Font = OsuFont.Default.With(size: 12f, weight: FontWeight.SemiBold),
                            },
                            strainGraph,
                        },
                    },
                },
            };

            var msdChart = new MsdBarChart
            {
                RelativeSizeAxes = Axes.X,
                Height = 65f,
            };

            var msdWedge = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                CornerRadius = 10,
                Masking = true,
                Alpha = 0f,
                Children = new Drawable[]
                {
                    new WedgeBackground(),
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Shear = -OsuGame.SHEAR,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0f, 4f),
                        Padding = new MarginPadding { Horizontal = 16f, Vertical = 12f },
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = "MSD Skillsets",
                                Font = OsuFont.Default.With(size: 12f, weight: FontWeight.SemiBold),
                            },
                            msdChart,
                        },
                    },
                },
            };

            var outerContainer = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0f, 4f),
                Shear = OsuGame.SHEAR,
                Padding = new MarginPadding { Top = 4f },
                Children = new Drawable[]
                {
                    new ShearAligningWrapper(strainWedge),
                    new ShearAligningWrapper(msdWedge),
                },
            };

            _setInternalChildMethod?.Invoke(__instance, new object[] { outerContainer });

            var state = new MetadataState(owner, beatmap, strainWedge, strainGraph, msdWedge, msdChart);
            _states.Add(__instance, state);

            var weakState = new WeakReference<MetadataState>(state);
            beatmap.BindValueChanged(_ =>
            {
                if (weakState.TryGetTarget(out var s))
                    s.TriggerRecalculate();
            }, true);

            SunnyState.Register(state);
        }
        catch (Exception e)
        {
            Trace.WriteLine($"[LazerSR] MetadataWedgePatch.Postfix failed: {e}");
        }
    }

    internal static void ScheduleOn(Drawable owner, Action action) =>
        _scheduleMethod?.Invoke(owner, new object[] { action });

    // ─── WedgeBackground ────────────────────────────────────────────────────
    private sealed class WedgeBackground : Box
    {
        public WedgeBackground()
        {
            RelativeSizeAxes = Axes.Both;
            Colour = new Color4(0.08f, 0.08f, 0.12f, 0.85f);
        }
    }

    // ─── MsdBarChart ─────────────────────────────────────────────────────────
    private sealed class MsdBarChart : CompositeDrawable
    {
        private readonly MsdCurve _curve;

        public MsdBarChart()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
                _curve = new MsdCurve { RelativeSizeAxes = Axes.Both },
            };
        }

        public void SetData(MsdData data) => _curve.SetData(data);

        private sealed class MsdCurve : Drawable
        {
            private const float MaxVal = 40f;
            private static readonly Color4[] SkillColors =
            {
                new Color4(0.69f, 0.69f, 0.69f, 1f), // Overall
                new Color4(0.36f, 0.78f, 0.96f, 1f), // Stream
                new Color4(0.44f, 0.87f, 0.48f, 1f), // Jumpstream
                new Color4(0.96f, 0.66f, 0.31f, 1f), // Handstream
                new Color4(0.76f, 0.45f, 0.94f, 1f), // Stamina
                new Color4(0.94f, 0.39f, 0.39f, 1f), // Jackspeed
                new Color4(0.96f, 0.83f, 0.31f, 1f), // Chordjack
                new Color4(0.31f, 0.96f, 0.83f, 1f), // Technical
            };

            private float[] _values = new float[8];
            private IShader _shader = null!;

            [BackgroundDependencyLoader]
            private void load(ShaderManager shaders) =>
                _shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, "FastCircle");

            public void SetData(MsdData data)
            {
                _values = new float[]
                {
                    data.Overall, data.Stream, data.Jumpstream, data.Handstream,
                    data.Stamina, data.Jackspeed, data.Chordjack, data.Technical,
                };
                Invalidate(Invalidation.DrawNode);
            }

            protected override DrawNode CreateDrawNode() => new MsdDrawNode(this);

            private sealed class MsdDrawNode : DrawNode
            {
                private readonly MsdCurve source;
                private Vector2 drawSize;
                private float[] values = new float[8];
                private IShader shader = null!;
                private IVertexBatch<TexturedVertex2D>? batch;

                public MsdDrawNode(MsdCurve source) : base(source) { this.source = source; }

                public override void ApplyState()
                {
                    base.ApplyState();
                    drawSize = source.DrawSize;
                    values = source._values;
                    shader = source._shader;
                }

                protected override void Draw(IRenderer renderer)
                {
                    base.Draw(renderer);

                    if (batch == null)
                        batch = renderer.CreateQuadBatch<TexturedVertex2D>(32, 1);

                    float padX = 6f;
                    float padBottom = 6f;
                    float plotW = Math.Max(1f, drawSize.X - padX);
                    float plotH = Math.Max(1f, drawSize.Y - padBottom);
                    float barW = plotW / 8f;
                    float alpha = DrawColourInfo.Colour.TopLeft.SRGB.A;

                    shader.Bind();

                    for (int i = 0; i < 8; i++)
                    {
                        float ratio = Math.Clamp(values[i] / MaxVal, 0f, 1f);
                        float barH = Math.Max(plotH * ratio, 1f);
                        float x = padX / 2f + i * barW;
                        var c = SkillColors[i];
                        var col = new Color4(c.R, c.G, c.B, c.A * alpha);
                        var rect = new RectangleF(x, drawSize.Y - padBottom - barH, barW - 1f, barH + padBottom);
                        var quad = Quad.FromRectangle(rect) * DrawInfo.Matrix;
                        var texRect = new Vector4(0, 0, rect.Width, rect.Height);
                        var blend = new Vector2(0.5f);

                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.BottomLeft,  TexturePosition = new Vector2(0, rect.Height),         TextureRect = texRect, BlendRange = blend, Colour = col });
                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.BottomRight, TexturePosition = new Vector2(rect.Width, rect.Height), TextureRect = texRect, BlendRange = blend, Colour = col });
                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.TopRight,    TexturePosition = new Vector2(rect.Width, 0),           TextureRect = texRect, BlendRange = blend, Colour = col });
                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.TopLeft,     TexturePosition = Vector2.Zero,                        TextureRect = texRect, BlendRange = blend, Colour = col });
                    }

                    shader.Unbind();
                }

                protected override void Dispose(bool isDisposing)
                {
                    base.Dispose(isDisposing);
                    batch?.Dispose();
                }
            }
        }
    }

    private sealed class MetadataState : ISunnyStateSubscriber
    {
        private readonly Drawable owner;
        private readonly IBindable<WorkingBeatmap> beatmap;
        private readonly Container strainWedge;
        private readonly StrainAreaGraph strainGraph;
        private readonly Container msdWedge;
        private readonly MsdBarChart msdChart;
        private CancellationTokenSource? cts;
        private readonly object _ctsLock = new();

        public MetadataState(Drawable owner, IBindable<WorkingBeatmap> beatmap,
            Container strainWedge, StrainAreaGraph strainGraph,
            Container msdWedge, MsdBarChart msdChart)
        {
            this.owner = owner;
            this.beatmap = beatmap;
            this.strainWedge = strainWedge;
            this.strainGraph = strainGraph;
            this.msdWedge = msdWedge;
            this.msdChart = msdChart;
        }

        public void TriggerRecalculate()
        {
            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Cancel();
                cts?.Dispose();
                cts = new CancellationTokenSource();
                token = cts.Token;
            }

            ScheduleOn(owner, () => strainWedge.Alpha = 0f);
            ScheduleOn(owner, () => msdWedge.Alpha = 0f);

            var wb = beatmap.Value;
            if (wb == null) return;
            if (wb.BeatmapInfo.Ruleset?.ShortName != "mania") return;

            Task.Run(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    // Strain graph
                    if (SunnyState.StrainGraphEnabled)
                    {
                        var playable = wb.GetPlayableBeatmap(wb.BeatmapInfo.Ruleset, Array.Empty<Mod>(), token);
                        var graphData = SunnyStrainGraph.Calculate(playable, Array.Empty<Mod>(), token);
                        SunnyState.StrainData = graphData;

                        token.ThrowIfCancellationRequested();
                        ScheduleOn(owner, () =>
                        {
                            if (token.IsCancellationRequested) return;
                            strainGraph.Update(graphData, SunnyState.HoneySpotsEnabled);
                            strainGraph.Alpha = 1f;
                            strainWedge.Alpha = 1f;
                        });
                    }

                    // MSD bar chart
                    if (SunnyState.MsdEnabled)
                    {
                        var playable = wb.GetPlayableBeatmap(wb.BeatmapInfo.Ruleset, Array.Empty<Mod>(), token);
                        var msd = MsdCalculator.Calculate(playable);

                        token.ThrowIfCancellationRequested();
                        if (msd != null)
                        {
                            ScheduleOn(owner, () =>
                            {
                                if (token.IsCancellationRequested) return;
                                msdChart.SetData(msd);
                                msdWedge.Alpha = 1f;
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Trace.WriteLine($"[LazerSR] MetadataState recalc failed: {e}");
                }
            }, token);
        }

        public void OnEnabledChanged(bool enabled)
        {
            if (!enabled)
            {
                ScheduleOn(owner, () => strainWedge.Alpha = 0f);
                ScheduleOn(owner, () => msdWedge.Alpha = 0f);
            }
            else if (SunnyState.StrainGraphEnabled || SunnyState.MsdEnabled)
                TriggerRecalculate();
        }

        public void OnFeatureChanged(SunnyFeature feature, bool enabled)
        {
            switch (feature)
            {
                case SunnyFeature.Graph:
                case SunnyFeature.Honey:
                    TriggerRecalculate();
                    break;
                case SunnyFeature.Msd:
                    if (!enabled)
                        ScheduleOn(owner, () => msdWedge.Alpha = 0f);
                    else
                        TriggerRecalculate();
                    break;
            }
        }
    }
}
