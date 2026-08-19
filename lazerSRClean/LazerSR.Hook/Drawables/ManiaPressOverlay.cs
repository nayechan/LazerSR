using System;
using System.Collections.Generic;
using System.Globalization;
using LazerSR.Hook.Data;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.UI.Scrolling;
using osuTK.Graphics;

namespace LazerSR.Hook.Drawables;

/// <summary>
/// mania 컬럼 하나에 리플레이의 키 입력 구간을 직사각형으로 그린다 (리플레이 재생 전용).
///
/// 높이는 누른 시각~뗀 시각 그대로, 폭은 컬럼의 90%. 노트 판정을 일으킨 입력은 노란색,
/// 아무 노트에도 걸리지 않은 입력은 밝은 회색이다.
/// 노란 입력에는 판정 오차(ms)를 적는다 — 얼리는 파란색, 레이트는 빨간색.
///
/// <para>
/// 노랑/회색과 오차값은 전부 <see cref="ManiaJudgementSimulation"/>이 미리 돌려 받아온
/// <b>osu!의 실제 <c>JudgementResult</c></b>다. 자체 판정 계산은 하지 않는다.
/// </para>
///
/// <para>
/// 사각형은 노트 뒤(<c>UnderlayElements</c>)에, 숫자는 노트 앞(<c>HitObjectArea</c>)에 그린다.
/// 그래서 <see cref="TextLayer"/>만 별도 컨테이너로 떼어 부모가 다른 곳에 붙이도록 한다.
/// </para>
/// </summary>
internal partial class ManiaPressOverlay : CompositeDrawable
{
    private const float width_ratio = 0.9f;
    private const float text_size = 20f;
    private const float text_outline = 1.6f;
    private const float text_padding = 3f;

    /// <summary>이 높이보다 짧은 사각형에는 릴리즈 숫자를 적지 않는다 (두 숫자가 겹친다).</summary>
    private const float min_height_for_two_labels = text_size * 2.4f;

    /// <summary>판정 시각과 입력 시각이 같다고 볼 허용 오차(ms).</summary>
    private const double match_tolerance = 1.0;

    private const int initial_pool_size = 24;
    private const int max_pool_size = 512;

    private static readonly Color4 judged_colour = new Color4(255, 209, 61, 255);
    private static readonly Color4 unjudged_colour = new Color4(198, 198, 198, 255);
    private static readonly Color4 early_colour = new Color4(96, 179, 255, 255);
    private static readonly Color4 late_colour = new Color4(255, 96, 96, 255);

    /// <summary>숫자를 그릴 레이어. 부모가 노트보다 앞선 컨테이너에 붙여야 한다.</summary>
    public Container TextLayer { get; } = new Container { RelativeSizeAxes = Axes.Both };

    private readonly double[] starts;
    private readonly double[] ends;

    /// <summary>노트 판정을 일으킨 입력인지. 시뮬레이션 결과가 도착하면 채워진다.</summary>
    private readonly bool[] judged;

    /// <summary>헤드/단노트 판정 오차. <see cref="double.NaN"/>이면 표시하지 않는다.</summary>
    private readonly double[] headOffset;

    /// <summary>롱노트 릴리즈 판정 오차. <see cref="double.NaN"/>이면 표시하지 않는다.</summary>
    private readonly double[] tailOffset;

    /// <summary>헤드 판정으로 채택한 노트의 시각. 같은 순간의 강제 미스와 구분하는 데 쓴다.</summary>
    private readonly double[] headNoteTime;

    private readonly ScrollingHitObjectContainer hitObjectContainer;
    private readonly ManiaSimulationResult? simulation;
    private readonly int column;

    private readonly IBindable<ScrollingDirection> direction = new Bindable<ScrollingDirection>();

    // static bindable에 직접 구독하면 오버레이가 GC되지 않는다. 반드시 필드로 보관되는 사본을 거친다.
    private readonly BindableBool showBoxes = new BindableBool(true);
    private readonly BindableBool showNumbers = new BindableBool(true);
    private readonly List<Box> pool = new List<Box>();
    private readonly List<OutlinedNumber> headLabels = new List<OutlinedNumber>();
    private readonly List<OutlinedNumber> tailLabels = new List<OutlinedNumber>();

    private bool scrollingDown = true;
    private bool simulationApplied;

    public ManiaPressOverlay(ScrollingHitObjectContainer hitObjectContainer, int column, double[] starts, double[] ends, ManiaSimulationResult? simulation)
    {
        this.hitObjectContainer = hitObjectContainer;
        this.column = column;
        this.starts = starts;
        this.ends = ends;
        this.simulation = simulation;

        judged = new bool[starts.Length];
        headOffset = new double[starts.Length];
        tailOffset = new double[starts.Length];
        headNoteTime = new double[starts.Length];

        for (int i = 0; i < starts.Length; i++)
            headOffset[i] = tailOffset[i] = double.NaN;

        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(IScrollingInfo scrollingInfo)
    {
        for (int i = 0; i < initial_pool_size && i < starts.Length; i++)
            growBox();

        direction.BindTo(scrollingInfo.Direction);
        direction.BindValueChanged(onDirectionChanged, true);

        showBoxes.BindTo(ManiaOverlayVisibility.PressBoxes);
        showNumbers.BindTo(ManiaOverlayVisibility.OffsetNumbers);
    }

    private Anchor anchor => scrollingDown ? Anchor.BottomCentre : Anchor.TopCentre;

    private Box growBox()
    {
        var box = new Box
        {
            RelativeSizeAxes = Axes.X,
            Width = width_ratio,
            Anchor = anchor,
            Origin = anchor,
            Alpha = 0f,
        };

        pool.Add(box);
        AddInternal(box);
        return box;
    }

    /// <summary>헤드 숫자는 사각형 아래 모서리의 "약간 위"에 앉으므로 아래쪽을 기준으로 잡는다.</summary>
    private Anchor headOrigin => scrollingDown ? Anchor.BottomCentre : Anchor.TopCentre;

    /// <summary>릴리즈 숫자는 위 모서리의 "약간 아래"이므로 반대다.</summary>
    private Anchor tailOrigin => scrollingDown ? Anchor.TopCentre : Anchor.BottomCentre;

    private OutlinedNumber growLabel(List<OutlinedNumber> target, Anchor origin)
    {
        var label = new OutlinedNumber(text_size, text_outline)
        {
            Anchor = anchor,
            Origin = origin,
            Alpha = 0f,
        };

        target.Add(label);
        TextLayer.Add(label);
        return label;
    }

    private void onDirectionChanged(ValueChangedEvent<ScrollingDirection> e)
    {
        scrollingDown = e.NewValue != ScrollingDirection.Up;

        foreach (var box in pool)
            box.Anchor = box.Origin = anchor;

        foreach (var label in headLabels)
        {
            label.Anchor = anchor;
            label.Origin = headOrigin;
        }

        foreach (var label in tailLabels)
        {
            label.Anchor = anchor;
            label.Origin = tailOrigin;
        }
    }

    /// <summary>
    /// 시뮬레이션 결과를 입력 구간에 붙인다. 판정이 입력 때문에 일어났다면 그 판정의
    /// <c>TimeAbsolute</c>가 곧 그 입력의 누름(또는 뗌) 시각이므로 시각으로 짝을 짓는다.
    /// </summary>
    private void applySimulation()
    {
        simulationApplied = true;

        if (simulation?.Judgements == null)
            return;

        foreach (var judgement in simulation.Judgements)
        {
            if (judgement.Column != column)
                continue;

            if (judgement.IsTail)
            {
                int index = findClosest(ends, judgement.Time);

                if (index >= 0)
                    tailOffset[index] = judgement.Offset;
            }
            else
            {
                int index = findClosest(starts, judgement.Time);

                if (index < 0)
                    continue;

                // 같은 순간에 여러 판정이 몰릴 수 있다 — 노트락으로 앞 노트들이 강제 미스되기 때문.
                // 그 입력이 실제로 겨냥한 노트는 그중 가장 늦은 노트다.
                if (judged[index] && judgement.NoteTime <= headNoteTime[index])
                    continue;

                judged[index] = true;
                headNoteTime[index] = judgement.NoteTime;
                headOffset[index] = judgement.Offset;
            }
        }

    }

    /// <summary><paramref name="time"/>과 <see cref="match_tolerance"/> 안에서 가장 가까운 원소의 인덱스. 없으면 -1.</summary>
    private static int findClosest(double[] values, double time)
    {
        int index = lowerBound(values, time);
        int best = -1;
        double bestDistance = match_tolerance;

        for (int i = Math.Max(0, index - 1); i <= index && i < values.Length; i++)
        {
            double distance = Math.Abs(values[i] - time);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    protected override void Update()
    {
        base.Update();

        if (!simulationApplied && simulation?.Ready == true)
            applySimulation();

        // 숫자는 레이어 하나로 묶여 있어 한 번에 끌 수 있다.
        TextLayer.Alpha = showNumbers.Value ? 1f : 0f;

        float scrollLength = DrawHeight;

        if (scrollLength <= 0)
        {
            hide(0, 0, 0);
            return;
        }

        double now = Time.Current;
        int used = 0;
        int headsUsed = 0;
        int tailsUsed = 0;

        // 아직 떼지 않은(= 끝이 미래인) 입력부터 본다 — 시작이 과거인 긴 누름도 포함해야 한다.
        for (int i = lowerBound(ends, now); i < starts.Length; i++)
        {
            float yStart = hitObjectContainer.PositionAtTime(starts[i]);
            float yEnd = hitObjectContainer.PositionAtTime(ends[i]);

            // 시작이 화면 바깥(미래 쪽)까지 밀려났으면 이후 입력도 전부 그렇다.
            if (yStart * (scrollingDown ? -1f : 1f) > scrollLength)
                break;

            // 판정선을 지난 부분은 그리지 않는다.
            float clampedStart = scrollingDown ? Math.Min(yStart, 0f) : Math.Max(yStart, 0f);

            if (used >= pool.Count && pool.Count >= max_pool_size)
                break;

            var box = used < pool.Count ? pool[used] : growBox();
            used++;

            float height = Math.Abs(yEnd - clampedStart);

            box.Y = clampedStart;
            box.Height = height;
            box.Colour = judged[i] ? judged_colour : unjudged_colour;
            box.Alpha = showBoxes.Value ? 1f : 0f;

            if (!judged[i])
                continue;

            // 숫자는 잘라내기 전 원래 위치에 붙인다 — 판정선을 지나면 사각형과 함께 사라진다.
            if (!double.IsNaN(headOffset[i]) && Math.Abs(yStart - clampedStart) < 0.5f)
            {
                var label = headsUsed < headLabels.Count ? headLabels[headsUsed] : growLabel(headLabels, headOrigin);
                headsUsed++;

                setLabel(label, headOffset[i], yStart + (scrollingDown ? -text_padding : text_padding));
            }

            if (!double.IsNaN(tailOffset[i]) && height >= min_height_for_two_labels)
            {
                var label = tailsUsed < tailLabels.Count ? tailLabels[tailsUsed] : growLabel(tailLabels, tailOrigin);
                tailsUsed++;

                setLabel(label, tailOffset[i], yEnd + (scrollingDown ? text_padding : -text_padding));
            }
        }

        hide(used, headsUsed, tailsUsed);
    }

    private static void setLabel(OutlinedNumber label, double offset, float y)
    {
        label.Y = y;
        label.Text = Math.Abs(Math.Round(offset)).ToString(CultureInfo.InvariantCulture);
        label.TextColour = offset < 0 ? early_colour : late_colour;
        label.Alpha = 1f;
    }

    private void hide(int boxes, int heads, int tails)
    {
        for (int i = boxes; i < pool.Count; i++)
            pool[i].Alpha = 0f;

        for (int i = heads; i < headLabels.Count; i++)
            headLabels[i].Alpha = 0f;

        for (int i = tails; i < tailLabels.Count; i++)
            tailLabels[i].Alpha = 0f;
    }

    /// <summary>
    /// <paramref name="time"/> 이상인 첫 원소의 인덱스. 되감기/시크에도 상태 없이 올바르게 동작한다.
    /// </summary>
    private static int lowerBound(double[] values, double time)
    {
        int lo = 0;
        int hi = values.Length;

        while (lo < hi)
        {
            int mid = (lo + hi) / 2;

            if (values[mid] < time)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}
