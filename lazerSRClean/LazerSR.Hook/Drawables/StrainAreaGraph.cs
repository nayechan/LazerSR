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

    // 개인화 diff 오버레이 - 만인/개인 strain을 집합처럼 겹쳐 본다.
    // 교집합(min)은 위 unplayedCurve/playedCurve가 그대로 그린다("흰색 그대로").
    // 만인이 더 큰 만큼(내가 남들보다 잘 치는 부분)은 파랑, 개인이 더 큰 만큼(못 치는 부분)은 빨강 - 그 위에 얹는 캡.
    private readonly StrainCapCurve strongerCaps; // 만인 > 개인
    private readonly StrainCapCurve weakerCaps;   // 개인 > 만인

    private readonly Container honeySpot2Overlays;
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
            strongerCaps = new StrainCapCurve
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0.25f, 0.55f, 1f, 1f),
            },
            weakerCaps = new StrainCapCurve
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(1f, 0.25f, 0.25f, 1f),
            },
            honeySpot2Overlays = new Container { RelativeSizeAxes = Axes.Both },
        };
    }

    public Func<double?>? CurrentTimeProvider { get; set; }

    /// <summary>
    /// <paramref name="universal"/>(만인 sunny+)과 <paramref name="personal"/>(만인+개인)을 겹쳐 그린다.
    /// 두 시간축은 같은 비트맵/모드에서 나온 것이라 항상 일치한다고 가정하지만, 방어적으로 짧은 쪽에 맞춘다.
    /// </summary>
    internal void Update(StrainGraphData universal, StrainGraphData personal, double[] honeySpots2)
    {
        if (universal.Strain.Length == 0 || personal.Strain.Length == 0)
        {
            unplayedCurve.Data = [];
            playedCurve.Data = [];
            strongerCaps.Data = [];
            weakerCaps.Data = [];
            honeySpot2Overlays.Clear();
            startTime = endTime = 0;
            return;
        }

        // 두 곡선을 같은 분모로 화면 높이에 맞출 뿐 - 모양을 서로 맞추는 정규화는 하지 않는다.
        double maxStrain = 0;
        foreach (var s in universal.Strain) if (s > maxStrain) maxStrain = s;
        foreach (var s in personal.Strain) if (s > maxStrain) maxStrain = s;

        int n = Math.Min(Math.Min(universal.Strain.Length, personal.Strain.Length), 300);
        double stepU = (double)universal.Strain.Length / n;
        double stepP = (double)personal.Strain.Length / n;

        float[] baseNorm = new float[n];
        var strongerSeg = new (float Low, float High)[n];
        var weakerSeg = new (float Low, float High)[n];

        for (int i = 0; i < n; i++)
        {
            int su = (int)(i * stepU);
            int sp = (int)(i * stepP);
            float u = maxStrain > 0 ? MathF.Pow((float)(universal.Strain[su] / maxStrain), 2.2f) : 0f;
            float p = maxStrain > 0 ? MathF.Pow((float)(personal.Strain[sp] / maxStrain), 2.2f) : 0f;

            float lo = Math.Min(u, p);
            float hi = Math.Max(u, p);
            baseNorm[i] = lo;

            if (u > p)
                strongerSeg[i] = (lo, hi);
            else if (p > u)
                weakerSeg[i] = (lo, hi);
        }

        unplayedCurve.Data = baseNorm;
        playedCurve.Data = baseNorm;
        strongerCaps.Data = strongerSeg;
        weakerCaps.Data = weakerSeg;

        startTime = universal.Times[0];
        endTime = universal.Times[^1];

        honeySpot2Overlays.Clear();
        if (honeySpots2.Length > 0 && universal.Times.Length > 0)
        {
            double totalMs2 = universal.Times[^1] - universal.Times[0];
            if (totalMs2 > 0)
            {
                foreach (var (start, end) in MergeSegments(honeySpots2, RESAMPLE_MS))
                {
                    float x = (float)((start - universal.Times[0]) / totalMs2);
                    float width = Math.Max(0.01f, (float)((end + RESAMPLE_MS - start) / totalMs2));
                    honeySpot2Overlays.Add(new Box
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

                float padX = 0f;
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

    /// <summary>
    /// <see cref="StrainCurve"/>와 같은 그리기 방식이지만 항상 바닥에서 시작하지 않고
    /// [Low, High] 구간만 뜬 막대를 그린다 - 만인/개인 strain의 차집합 "캡"용.
    /// </summary>
    private sealed class StrainCapCurve : Drawable
    {
        private (float Low, float High)[] data = [];
        public (float Low, float High)[] Data
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

        protected override DrawNode CreateDrawNode() => new CapDrawNode(this);

        private sealed class CapDrawNode : DrawNode
        {
            private readonly StrainCapCurve source;
            private Vector2 drawSize;
            private (float Low, float High)[] data = [];
            private IShader shader = null!;
            private IVertexBatch<TexturedVertex2D>? batch;
            private int batchCapacity;

            public CapDrawNode(StrainCapCurve source) : base(source) { this.source = source; }

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

                float padX = 0f;
                float padTop = 8f;
                float padBottom = 6f;
                float plotW = Math.Max(1f, drawSize.X - padX);
                float plotH = Math.Max(1f, drawSize.Y - padTop - padBottom);
                float barWidth = plotW / data.Length;
                shader.Bind();

                for (int i = 0; i < data.Length; i++)
                {
                    var (low, high) = data[i];
                    if (high <= low) continue;

                    // StrainDrawNode와 같은 바닥 기준(drawSize.Y - padBottom)에서 재는 두 높이 - 캡이
                    // 아래쪽 StrainCurve(값=low까지)와 이가 맞물리게 정렬된다.
                    float lowHeight = Math.Max(plotH * low, 1f);
                    float highHeight = Math.Max(plotH * high, 1f);
                    float x = padX + i * barWidth;
                    var rect = new RectangleF(x, drawSize.Y - padBottom - highHeight, barWidth + 1, highHeight - lowHeight);
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
