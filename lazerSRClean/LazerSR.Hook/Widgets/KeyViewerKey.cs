using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens.Play.HUD;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Widgets;

/// <summary>
/// 키뷰어의 컬럼 하나. 아래에 키 박스, 위에 막대가 흐르는 영역.
/// </summary>
internal partial class KeyViewerKey : KeyCounter
{
    private const float border_thickness = 2f;
    private const float corner_radius = 3f;
    private const double fill_fade_ms = 30;

    /// <summary>보이는 영역을 이만큼 더 지나야 막대를 버린다.</summary>
    private const float cull_margin = 200f;

    /// <summary>이보다 큰 시간 도약은 재생이 아니라 시크로 본다.</summary>
    private const double warp_threshold_ms = 1000;

    /// <summary>판정이 왔는지 확인할 프레임 수. mania는 누르는 프레임에 판정이 나므로 여유분만 둔다.</summary>
    private const int judgement_wait_frames = 2;

    private sealed class Bar
    {
        public Box Drawable = null!;
        public double StartTime;
        public double? EndTime;

        /// <summary>이 입력이 실제로 노트 판정을 일으켰는지 확정됐는가.</summary>
        public bool Resolved;

        /// <summary>확정 결과. 판정을 일으켰으면 true.</summary>
        public bool Live = true;

        public int FramesAlive;

        /// <summary>이 입력이 시작될 때 그 컬럼의 누적 판정 수.</summary>
        public int JudgementCountAtStart;
    }

    private readonly KeyViewerWidget owner;
    private readonly int keyNumber;
    private readonly int column;

    private readonly List<Bar> bars = new List<Bar>();
    private Bar? growing;

    private Container? barArea;
    private Container box = null!;
    private Box fill = null!;
    private OsuSpriteText label = null!;

    private double lastTime = double.NaN;

    public KeyViewerKey(KeyViewerWidget owner, InputTrigger trigger, int keyNumber, int column)
        : base(trigger)
    {
        this.owner = owner;
        this.keyNumber = keyNumber;
        this.column = column;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            barArea = new Container
            {
                RelativeSizeAxes = Axes.X,
                // 사용자가 정한 높이에서 잘린다.
                Masking = true,
            },
            box = new Container
            {
                Masking = true,
                CornerRadius = corner_radius,
                BorderThickness = border_thickness,
                Children = new Drawable[]
                {
                    fill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0f,
                    },
                    label = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = keyNumber.ToString(CultureInfo.InvariantCulture),
                    },
                },
            },
        };

        IsActive.BindValueChanged(onActiveChanged, true);
    }

    private void onActiveChanged(ValueChangedEvent<bool> e) => fill.FadeTo(e.NewValue ? 1f : 0f, fill_fade_ms);

    protected override void Activate(bool forwardPlayback = true)
    {
        base.Activate(forwardPlayback);

        if (!forwardPlayback || barArea == null)
            return;

        endGrowing();

        growing = new Bar
        {
            Drawable = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 0,
            },
            StartTime = Time.Current,
            JudgementCountAtStart = owner.JudgementCount(column),
        };

        bars.Add(growing);
        barArea.Add(growing.Drawable);
    }

    protected override void Deactivate(bool forwardPlayback = true)
    {
        base.Deactivate(forwardPlayback);

        endGrowing();
    }

    private void endGrowing()
    {
        if (growing == null)
            return;

        growing.EndTime = Time.Current;
        growing = null;
    }

    /// <summary>
    /// 이 입력이 노트 판정을 일으켰는지 확정한다. mania는 누르는 프레임에 바로 판정이 나므로,
    /// 몇 프레임만 기다렸다가 그 컬럼의 판정 카운터가 늘었는지 보면 된다.
    /// 확정 전에는 살아있는 것으로 간주해 죽은 입력만 잠깐 늦게 표시된다.
    /// </summary>
    private void resolveLiveness(Bar bar, bool markDead)
    {
        if (bar.Resolved || !markDead)
            return;

        bar.FramesAlive++;

        if (owner.JudgementCount(column) > bar.JudgementCountAtStart)
        {
            bar.Live = true;
            bar.Resolved = true;
        }
        else if (bar.FramesAlive >= judgement_wait_frames)
        {
            bar.Live = false;
            bar.Resolved = true;
        }
    }

    private void clearBars()
    {
        if (barArea == null)
            return;

        foreach (var bar in bars)
            barArea.Remove(bar.Drawable, true);

        bars.Clear();
        growing = null;
    }

    protected override void Update()
    {
        base.Update();

        if (barArea == null)
            return;

        float keySize = owner.KeySize.Value;
        float areaHeight = owner.BarAreaHeight.Value;
        var outlineColour = owner.OutlineColour.Value;
        var activeColour = owner.ActiveColour.Value;
        bool downward = owner.Downward.Value;
        bool markDead = owner.ShowDeadPresses.Value;

        // 아래로 흐를 때는 키 박스가 위로 가고 막대 영역이 아래로 간다.
        var barAnchor = downward ? Anchor.TopCentre : Anchor.BottomCentre;

        barArea.Anchor = barArea.Origin = downward ? Anchor.BottomCentre : Anchor.TopCentre;
        box.Anchor = box.Origin = barAnchor;

        Size = new Vector2(keySize, keySize + areaHeight);
        barArea.Height = areaHeight;
        box.Size = new Vector2(keySize);
        box.BorderColour = outlineColour;
        label.Font = OsuFont.Torus.With(size: keySize * 0.45f, weight: FontWeight.SemiBold);
        label.Colour = outlineColour;
        fill.Colour = activeColour;

        double now = Time.Current;

        // 시크·되감기는 흐르고 있는 막대를 전부 무의미하게 만든다. 지우고 그 지점부터 다시 시작한다.
        // 배속은 여기 안 걸린다 — 프레임당 경과가 배속만큼 늘어날 뿐이다.
        if (double.IsFinite(lastTime))
        {
            double elapsed = now - lastTime;

            if (elapsed < 0 || elapsed > warp_threshold_ms)
                clearBars();
        }

        lastTime = now;

        float pixelsPerMs = owner.ScrollSpeed.Value / 1000f;

        for (int i = bars.Count - 1; i >= 0; i--)
        {
            var bar = bars[i];

            // 아직 누르고 있는 막대는 끝이 "지금"이다 — 시작 면이 키 박스에 붙어 있고 반대 면만 자란다.
            double end = bar.EndTime ?? now;

            float height = (float)Math.Max(0, (end - bar.StartTime) * pixelsPerMs);

            // 시작 면이 키 박스에서 떨어진 거리. 항상 0 이상이고 방향은 앵커가 정한다.
            float distance = (float)Math.Max(0, (now - end) * pixelsPerMs);

            resolveLiveness(bar, markDead);

            bar.Drawable.Anchor = bar.Drawable.Origin = barAnchor;
            bar.Drawable.Height = height;
            bar.Drawable.Y = downward ? distance : -distance;
            bar.Drawable.Colour = markDead && !bar.Live ? outlineColour : activeColour;

            // 시작 면이 보이는 영역 밖으로 완전히 빠져나갔으면 버린다.
            if (distance > areaHeight + cull_margin)
            {
                barArea.Remove(bar.Drawable, true);
                bars.RemoveAt(i);
            }
        }
    }
}
