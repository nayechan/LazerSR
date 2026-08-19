using System;
using System.Collections.Generic;
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
/// mania 컬럼 하나에 "실제 판정 기준선"을 그린다 (리플레이/관전 전용).
///
/// - 정지선 1개: 판정이 실제로 일어나는 위치. 컬럼 폭 전체.
/// - 가로선 N개: 단노트와 롱노트 헤드의 판정 시각. 컬럼 폭 전체.
/// - 롱노트마다: 릴리즈 판정 시각의 가로선(컬럼 폭 70%) + 시작~끝을 잇는 세로선.
///
/// 위치는 osu! 본체가 노트를 배치할 때 쓰는 것과 같은
/// <see cref="ScrollingHitObjectContainer.PositionAtTime(double)"/>을 그대로 쓴다.
/// 따라서 스크롤 속도·SV·재생 배속이 전부 자동으로 반영된다.
/// </summary>
internal partial class ManiaJudgementLineOverlay : CompositeDrawable
{
    private const float line_thickness = 2f;
    private const float tail_width_ratio = 0.7f;
    private const int initial_pool_size = 32;
    private const int max_pool_size = 512;

    /// <summary>단노트 + 롱노트 헤드의 판정 시각. 오름차순.</summary>
    private readonly double[] times;

    /// <summary>롱노트 시작 시각. <see cref="holdEnds"/>와 같은 인덱스로 짝을 이룬다.</summary>
    private readonly double[] holdStarts;

    /// <summary>롱노트 릴리즈 판정 시각. 오름차순(같은 컬럼의 롱노트는 겹칠 수 없다).</summary>
    private readonly double[] holdEnds;

    private readonly ScrollingHitObjectContainer hitObjectContainer;

    private readonly IBindable<ScrollingDirection> direction = new Bindable<ScrollingDirection>();

    // static bindable에 직접 구독하면 오버레이가 GC되지 않는다. 반드시 필드로 보관되는 사본을 거친다.
    private readonly BindableBool showNoteLines = new BindableBool(true);
    private readonly BindableBool showJudgementLine = new BindableBool(true);

    private readonly List<Box> linePool = new List<Box>();
    private readonly List<Box> tailPool = new List<Box>();
    private readonly List<Box> connectorPool = new List<Box>();

    private Box staticLine = null!;

    private bool scrollingDown = true;

    public ManiaJudgementLineOverlay(ScrollingHitObjectContainer hitObjectContainer, double[] times, double[] holdStarts, double[] holdEnds)
    {
        this.hitObjectContainer = hitObjectContainer;
        this.times = times;
        this.holdStarts = holdStarts;
        this.holdEnds = holdEnds;

        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(IScrollingInfo scrollingInfo)
    {
        AddInternal(staticLine = createHorizontal(1f));

        for (int i = 0; i < initial_pool_size; i++)
        {
            grow(linePool, () => createHorizontal(1f));

            if (holdEnds.Length > 0)
            {
                grow(tailPool, () => createHorizontal(tail_width_ratio));
                grow(connectorPool, createConnector);
            }
        }

        direction.BindTo(scrollingInfo.Direction);
        direction.BindValueChanged(onDirectionChanged, true);

        showNoteLines.BindTo(ManiaOverlayVisibility.NoteLines);
        showJudgementLine.BindTo(ManiaOverlayVisibility.JudgementLine);
    }

    private Box createHorizontal(float widthRatio) => new Box
    {
        RelativeSizeAxes = Axes.X,
        Width = widthRatio,
        Height = line_thickness,
        Colour = Color4.White,
        Anchor = scrollingDown ? Anchor.BottomCentre : Anchor.TopCentre,
        Origin = Anchor.Centre,
        Alpha = 0f,
    };

    private Box createConnector() => new Box
    {
        Width = line_thickness,
        Colour = Color4.White,
        Anchor = scrollingDown ? Anchor.BottomCentre : Anchor.TopCentre,
        Origin = scrollingDown ? Anchor.BottomCentre : Anchor.TopCentre,
        Alpha = 0f,
    };

    private Box grow(List<Box> pool, Func<Box> factory)
    {
        var box = factory();
        pool.Add(box);
        AddInternal(box);
        return box;
    }

    private void onDirectionChanged(ValueChangedEvent<ScrollingDirection> e)
    {
        scrollingDown = e.NewValue != ScrollingDirection.Up;

        var anchor = scrollingDown ? Anchor.BottomCentre : Anchor.TopCentre;

        staticLine.Anchor = anchor;

        foreach (var box in linePool)
            box.Anchor = anchor;

        foreach (var box in tailPool)
            box.Anchor = anchor;

        foreach (var box in connectorPool)
            box.Anchor = box.Origin = anchor;
    }

    protected override void Update()
    {
        base.Update();

        // 정지선은 항상 스크롤 원점(= 판정 지점)에 놓인다.
        // HitObjectArea가 이미 히트포지션 패딩을 적용한 좌표계라 Y=0이 곧 진짜 판정선이며,
        // 스킨이 HitPosition을 바꿔도 자동으로 따라간다.
        staticLine.Y = 0f;
        staticLine.Alpha = showJudgementLine.Value ? 1f : 0f;

        float scrollLength = DrawHeight;

        if (scrollLength <= 0 || !showNoteLines.Value)
        {
            hideFrom(linePool, 0);
            hideFrom(tailPool, 0);
            hideFrom(connectorPool, 0);
            return;
        }

        double now = Time.Current;

        updateLines(scrollLength, now);
        updateHolds(scrollLength, now);
    }

    private void updateLines(float scrollLength, double now)
    {
        int used = 0;

        for (int i = lowerBound(times, now); i < times.Length; i++)
        {
            float y = hitObjectContainer.PositionAtTime(times[i]);

            // 시각이 오름차순이면 |y|도 단조증가하므로 화면을 벗어나는 순간 중단할 수 있다.
            if (Math.Abs(y) > scrollLength)
                break;

            if (!tryTake(linePool, used, () => createHorizontal(1f), out var box))
                break;

            used++;
            box.Y = y;
            box.Alpha = 1f;
        }

        hideFrom(linePool, used);
    }

    private void updateHolds(float scrollLength, double now)
    {
        int used = 0;

        // 아직 끝나지 않은 롱노트부터 본다 — 이미 붙잡고 있는(시작이 과거인) 롱노트도 포함해야 한다.
        for (int i = lowerBound(holdEnds, now); i < holdStarts.Length; i++)
        {
            float yStart = hitObjectContainer.PositionAtTime(holdStarts[i]);
            float yEnd = hitObjectContainer.PositionAtTime(holdEnds[i]);

            // 미래는 다운스크롤에서 음수, 업스크롤에서 양수 방향이다.
            // 시작이 화면 바깥(미래 쪽)까지 밀려났으면 이후 롱노트도 전부 그렇다.
            // |y| 비교가 아니라 부호를 봐야 화면보다 긴 롱노트를 붙잡고 있는 동안 끊기지 않는다.
            if (yStart * (scrollingDown ? -1f : 1f) > scrollLength)
                break;

            // 판정선을 지나간 부분은 그리지 않는다 (롱노트 바디가 소진되는 것과 같은 표현).
            if (scrollingDown)
                yStart = Math.Min(yStart, 0f);
            else
                yStart = Math.Max(yStart, 0f);

            float height = Math.Abs(yEnd - yStart);

            if (!tryTake(tailPool, used, () => createHorizontal(tail_width_ratio), out var tail))
                break;

            if (!tryTake(connectorPool, used, createConnector, out var connector))
                break;

            used++;

            tail.Y = yEnd;
            tail.Alpha = 1f;

            connector.Y = yStart;
            connector.Height = height;
            connector.Alpha = height > 0 ? 1f : 0f;
        }

        hideFrom(tailPool, used);
        hideFrom(connectorPool, used);
    }

    private bool tryTake(List<Box> pool, int index, Func<Box> factory, out Box box)
    {
        if (index < pool.Count)
        {
            box = pool[index];
            return true;
        }

        if (pool.Count >= max_pool_size)
        {
            box = null!;
            return false;
        }

        box = grow(pool, factory);
        return true;
    }

    private static void hideFrom(List<Box> pool, int start)
    {
        // 조기 중단하지 않는다 — 길이 0인 롱노트 때문에 사용 구간 중간에도 Alpha 0이 나올 수 있다.
        // Drawable.Alpha는 같은 값을 다시 넣으면 no-op이라 비용은 없다.
        for (int i = start; i < pool.Count; i++)
            pool[i].Alpha = 0f;
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
