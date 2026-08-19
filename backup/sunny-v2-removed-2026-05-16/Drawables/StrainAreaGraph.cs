using System;
using System.Collections.Generic;
using System.Linq;
using LazerSR.Hook.Data;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Drawables;

internal sealed class StrainAreaGraph : CompositeDrawable
{
    private const double RESAMPLE_MS = 100.0;
    private readonly StrainCurve unplayedCurve;
    private readonly StrainCurve playedCurve;
    private readonly Container playedMask;
    private readonly Container honeyOverlays;
    private double startTime;
    private double endTime;

    public StrainAreaGraph()
    {
        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.Black,
            },
            unplayedCurve = new StrainCurve
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0.10f, 0.10f, 0.10f, 1f),
            },
            playedMask = new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = 0,
                Masking = true,
                Child = playedCurve = new StrainCurve
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 0,
                    Colour = new Color4(1f, 1f, 1f, 1f),
                },
            },
            honeyOverlays = new Container { RelativeSizeAxes = Axes.Both },
        };
    }

    public Func<double?>? CurrentTimeProvider { get; set; }

    internal void Update(StrainGraphData data, bool showHoney)
    {
        if (data.Strain.Length == 0)
        {
            unplayedCurve.Data = [];
            playedCurve.Data = [];
            honeyOverlays.Clear();
            startTime = endTime = 0;
            return;
        }

        double maxStrain = 0;
        foreach (var s in data.Strain)
            if (s > maxStrain) maxStrain = s;

        int n = Math.Min(data.Strain.Length, 300);
        float[] normalized = new float[n];
        double step = (double)data.Strain.Length / n;
        for (int i = 0; i < n; i++)
        {
            int src = (int)(i * step);
            normalized[i] = maxStrain > 0 ? (float)(data.Strain[src] / maxStrain) : 0f;
        }

        unplayedCurve.Data = normalized;
        playedCurve.Data = normalized;
        startTime = data.Times[0];
        endTime = data.Times[^1];

        honeyOverlays.Clear();
        if (showHoney && data.HoneySpots.Length > 0 && data.Times.Length > 0)
        {
            double totalMs = data.Times[^1] - data.Times[0];
            if (totalMs > 0)
            {
                foreach (var (start, end) in MergeSegments(data.HoneySpots, RESAMPLE_MS))
                {
                    float x = (float)((start - data.Times[0]) / totalMs);
                    float width = Math.Max(0.01f, (float)((end + RESAMPLE_MS - start) / totalMs));
                    honeyOverlays.Add(new Box
                    {
                        RelativePositionAxes = Axes.X,
                        RelativeSizeAxes = Axes.Both,
                        X = Math.Clamp(x, 0f, 1f),
                        Width = Math.Min(width, 1f),
                        Colour = ColourInfo.GradientVertical(
                            new Color4(1f, 0.78f, 0f, 0f),
                            new Color4(1f, 0.78f, 0f, 0.55f)),
                    });
                }
            }
        }
    }

    protected override void Update()
    {
        base.Update();

        double? current = CurrentTimeProvider?.Invoke();
        if (current == null || endTime <= startTime)
        {
            playedMask.Width = 0;
            playedCurve.Width = DrawWidth;
            return;
        }

        float fullWidth = DrawWidth;
        playedCurve.Width = fullWidth;
        playedMask.Width = fullWidth * (float)Math.Clamp((current.Value - startTime) / (endTime - startTime), 0.0, 1.0);
    }

    private static IEnumerable<(double Start, double End)> MergeSegments(double[] points, double gapMs)
    {
        if (points.Length == 0)
            yield break;

        double[] sorted = points.ToArray();
        Array.Sort(sorted);

        double start = sorted[0];
        double end = sorted[0];
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i] - end <= gapMs)
            {
                end = sorted[i];
                continue;
            }

            yield return (start, end);
            start = end = sorted[i];
        }

        yield return (start, end);
    }

    private sealed class StrainCurve : Drawable
    {
        private float[] data = [];
        public float[] Data
        {
            get => data;
            set { data = value; Invalidate(Invalidation.DrawNode); }
        }

        private IShader shader = null!;

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, "FastCircle");
        }

        protected override DrawNode CreateDrawNode() => new StrainDrawNode(this);

        private sealed class StrainDrawNode : DrawNode
        {
            private readonly StrainCurve source;
            private Vector2 drawSize;
            private float[] data = [];
            private IShader shader = null!;
            private IVertexBatch<TexturedVertex2D>? batch;
            private int batchCapacity;

            public StrainDrawNode(StrainCurve source) : base(source) { this.source = source; }

            public override void ApplyState()
            {
                base.ApplyState();
                drawSize = source.DrawSize;
                data = source.data;
                shader = source.shader;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                if (data.Length == 0) return;

                if (batch == null || batchCapacity != data.Length)
                {
                    batch?.Dispose();
                    batch = renderer.CreateQuadBatch<TexturedVertex2D>(data.Length * 4, 1);
                    batchCapacity = data.Length;
                }

                float padX = 6f;
                float padTop = 8f;
                float padBottom = 6f;
                float plotW = Math.Max(1f, drawSize.X - padX);
                float plotH = Math.Max(1f, drawSize.Y - padTop - padBottom);
                float barWidth = plotW / data.Length;
                shader.Bind();

                for (int i = 0; i < data.Length; i++)
                {
                    float barHeight = Math.Max(plotH * data[i], 1f);
                    float x = padX + i * barWidth;
                    var rect = new RectangleF(x, drawSize.Y - padBottom - barHeight, barWidth + 1, barHeight + padBottom + 1);
                    var quad = Quad.FromRectangle(rect) * DrawInfo.Matrix;
                    var texRect = new Vector4(0, 0, rect.Width, rect.Height);
                    var blend = new Vector2(0.5f);
                    var col = DrawColourInfo.Colour;

                    batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.BottomLeft,  TexturePosition = new Vector2(0, rect.Height), TextureRect = texRect, BlendRange = blend, Colour = col.BottomLeft.SRGB });
                    batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.BottomRight, TexturePosition = new Vector2(rect.Width, rect.Height), TextureRect = texRect, BlendRange = blend, Colour = col.BottomRight.SRGB });
                    batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.TopRight,    TexturePosition = new Vector2(rect.Width, 0), TextureRect = texRect, BlendRange = blend, Colour = col.TopRight.SRGB });
                    batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.TopLeft,     TexturePosition = Vector2.Zero, TextureRect = texRect, BlendRange = blend, Colour = col.TopLeft.SRGB });
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
