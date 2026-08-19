using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Calculators;
using LazerSR.Hook.Screens;
using LazerSR.SunnyCalculator;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Drawables;

/// <summary>
/// 노트별 판정 오차를 시간축 위에 흩뿌린 산점도 (mania 전용, 결과창 통계 패널).
/// 가로축은 첫 노트~마지막 노트, 세로축 중앙이 오차 0이고 위가 얼리 / 아래가 레이트다.
/// 점 색은 osu! 관행(<see cref="OsuColour.ForHitResult"/>)을 그대로 따른다.
/// 하단 버튼 3개로 단노트 / 롱헤드 / 롱테일 출처를 각각 켜고 끈다.
/// <para>
/// 플롯 안에서 드래그하면 흰 세로선 두 개로 구간이 잡히고 <b>상세 모드</b>가 켜진다 — 좌상단에 그
/// 구간만의 정확도 3종, 우상단에 그 구간만으로 잰 sunnySR pill이 뜬다.
/// </para>
/// </summary>
internal sealed partial class ManiaJudgementScatterGraph : CompositeDrawable
{
    private const float axis_label_width = 34f;
    private const float time_label_height = 14f;
    private const float button_height = 22f;
    private const float button_row_height = button_height + 8f;
    private const float bottom_reserved = time_label_height + button_row_height;
    private const int time_label_count = 5;

    /// <summary>이보다 짧게 드래그하면 선택이 아니라 단순 클릭으로 본다.</summary>
    private const float min_selection_pixels = 4f;

    private const float accuracy_row_width = 128f;
    private const float accuracy_row_height = 15f;

    /// <summary>버튼 묶음 사이의 여백. <c>FillFlowContainer</c>의 기본 간격 위에 얹힌다.</summary>
    private const float button_group_gap = 12f;

    /// <summary>컬럼 버튼은 숫자 한 글자라 좁게 잡는다.</summary>
    private const float column_button_width = 26f;

    // 판정창 경계선을 그릴 등급. 안쪽부터 바깥쪽 순서.
    private static readonly HitResult[] window_results =
    [
        HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh
    ];

    private enum NoteKind
    {
        Single,
        Head,
        Tail,
    }

    private readonly ScoreInfo score;
    private readonly IReadOnlyList<HitEvent> hitEvents;
    private readonly IBeatmap playableBeatmap;
    private readonly Mod[] mods;

    private Container plotArea = null!;
    private Container offsetLabelLayer = null!;
    private Container timeLabelLayer = null!;
    private FillFlowContainer buttonRow = null!;

    private ScatterPoints? points;
    private (NoteKind Kind, int Column, ScatterPoints.Point Point)[] allPoints = [];

    // 토글 버튼은 필드로 붙잡는다 - 버튼이 들고 있는 BindableBool의 수명을 보장해야 한다.
    private ToggleButton singleButton = null!;
    private ToggleButton headButton = null!;
    private ToggleButton tailButton = null!;
    private ToggleButton[] columnButtons = [];

    // 구간 선택 / 상세 모드.
    // 정확도는 미스까지 세야 하므로 점으로 그리는 목록(allPoints)과 별개로 판정 전량을 들고 있는다.
    private HitEvent[] accuracyEvents = [];
    private double firstTime;
    private double timeSpan = 1;
    private double fullWeightedNoteCount;

    private Box selectionLineA = null!;
    private Box selectionLineB = null!;
    private Container detailLayer = null!;
    private OsuSpriteText legacyValue = null!;
    private OsuSpriteText lazerValue = null!;
    private OsuSpriteText ppValue = null!;
    private StarRatingDisplay sectionPill = null!;

    private float dragStartFraction;
    private float dragEndFraction;
    private CancellationTokenSource? sunnyCts;

    // 드래그로 잡은 시간 범위. 컬럼 버튼이 바뀌면 이 범위로 정확도를 다시 낸다.
    private (double Start, double End)? selectionRange;

    // 구간 연습. 실제 노트 기준으로 잡은 경계이며, 선택이 없으면 null.
    private PracticeButton practiceButton = null!;
    private (double Start, double End)? practiceSection;

    public ManiaJudgementScatterGraph(ScoreInfo score, IBeatmap playableBeatmap)
    {
        this.score = score;
        this.playableBeatmap = playableBeatmap;

        hitEvents = score.HitEvents;

        // 본 게임과 인스턴스를 공유하지 않는다 (safety.md "라이브 vs 시뮬레이션 인스턴스").
        mods = score.Mods.Select(m => m.DeepClone()).ToArray();
    }

    [BackgroundDependencyLoader]
    private void load(OsuColour colours)
    {
        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Bottom = bottom_reserved },
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = axis_label_width },
                        Child = plotArea = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 3,
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = OsuColour.Gray(0.12f),
                            },
                        },
                    },
                    // 오차 라벨은 플롯과 세로 범위가 정확히 같은 레이어에 둔다 - 전체 높이를
                    // 기준으로 배치하면 하단에 예약한 공간만큼 눈금이 어긋난다.
                    offsetLabelLayer = new Container { RelativeSizeAxes = Axes.Both },
                },
            },
            timeLabelLayer = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = time_label_height,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Padding = new MarginPadding { Left = axis_label_width },
                Margin = new MarginPadding { Bottom = button_row_height },
            },
            buttonRow = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Margin = new MarginPadding { Left = axis_label_width },
            },
        };

        // [단노트/롱헤드/롱테일] 여백 [1..n] 여백 [구간 연습].
        // 묶음 사이 여백은 첫 버튼의 Margin으로 준다 - 중첩 컨테이너를 만들지 않기 위함.
        buttonRow.AddRange(new Drawable[]
        {
            singleButton = new ToggleButton("단노트", colours.Blue),
            headButton = new ToggleButton("롱헤드", colours.Blue),
            tailButton = new ToggleButton("롱테일", colours.Blue),
        });

        columnButtons = createColumnButtons(colours);
        buttonRow.AddRange(columnButtons);

        buttonRow.Add(practiceButton = new PracticeButton("구간 연습", colours.Pink)
        {
            Action = startSectionPractice,
            Margin = new MarginPadding { Left = button_group_gap },
        });

        // 정확도 계산용 판정 목록. 그래프에는 안 그리지만 미스가 분모에 들어가야 하므로 여기서는
        // IsHit() 필터를 걸지 않는다. 부가 판정(롱노트 바디 등)은 정확도에 관여하지 않으므로 IsBasic()만 본다.
        accuracyEvents = hitEvents.Where(e => e.Result.IsBasic()).ToArray();

        // 미스는 제외한다 - JudgementResult.TimeOffset이 미스 판정창 값으로 클램프되므로
        // 실제 오차가 아니라 상수이고, 그대로 찍으면 가장자리에 직선 하나가 될 뿐이다.
        // (osu! 자신의 타이밍 분포 그래프도 같은 이유로 IsHit()으로 거른다.)
        // HoldNote 본체와 바디는 HitWindows가 Empty라 같은 조건에서 함께 걸러진다.
        var events = hitEvents
                     .Where(e => e.HitObject.HitWindows != HitWindows.Empty && e.Result.IsBasic() && e.Result.IsHit())
                     .OrderBy(e => e.HitObject.GetEndTime())
                     .ToArray();

        if (events.Length == 0)
            return;

        // TimeOffset은 비트맵 시간 기준이라 배속에 비례해 커진다. mania는 판정창도 같은 배속만큼
        // 늘어나므로(ManiaHitWindows.SpeedMultiplier) 원본 값끼리는 정합이 맞지만, 표시되는 ms가
        // 배속마다 달라진다. 실제로 몇 ms 어긋났는지를 보여주기 위해 오차와 판정창을 모두
        // 배속으로 나눠 실시간 기준으로 되돌린다.
        double rate = events.Select(e => e.GameplayRate ?? 1.0).FirstOrDefault(r => r > 0);
        if (!(rate > 0)) rate = 1.0;

        var windows = events[0].HitObject.HitWindows;
        double missWindow = windows.WindowFor(HitResult.Miss) / rate;
        if (!(missWindow > 0)) return;

        double lastTime = events[^1].HitObject.GetEndTime();
        firstTime = events[0].HitObject.GetEndTime();
        timeSpan = lastTime - firstTime;
        if (timeSpan <= 0) timeSpan = 1;

        addWindowLines(colours, windows, rate, missWindow);
        addTimeLabels(firstTime, lastTime);

        allPoints = new (NoteKind, int, ScatterPoints.Point)[events.Length];

        for (int i = 0; i < events.Length; i++)
        {
            var e = events[i];
            double offset = e.TimeOffset / rate;

            allPoints[i] = (kindOf(e.HitObject), columnOf(e.HitObject), new ScatterPoints.Point(
                (float)((e.HitObject.GetEndTime() - firstTime) / timeSpan),
                // 화면 좌표는 아래로 갈수록 커진다 - 레이트(양수 오차)가 아래가 되도록 그대로 더한다.
                (float)((offset + missWindow) / (2 * missWindow)),
                colours.ForHitResult(e.Result)));
        }

        plotArea.Add(points = new ScatterPoints { RelativeSizeAxes = Axes.Both });

        // 짧은 맵 보정에 대입할 전체 맵 기준값. 구간이 바뀌어도 변하지 않으므로 한 번만 구한다.
        // (BDL은 백그라운드 로드 스레드에서 돌기 때문에 여기서 계산해도 화면이 멈추지 않는다.)
        fullWeightedNoteCount = SunnyManiaDifficultyCalculator.CalculateWeightedNoteCount(playableBeatmap, mods);

        addSelectionLayers();
    }

    /// <summary>
    /// 선택선 → 상세 표시 → 입력 레이어 순으로 쌓는다. 입력 레이어가 맨 위여야 플롯 어디를 눌러도
    /// 드래그가 잡힌다 (아래 요소들은 입력을 받지 않는 순수 표시물이라 가려도 무해하다).
    /// </summary>
    private void addSelectionLayers()
    {
        plotArea.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                selectionLineA = createSelectionLine(),
                selectionLineB = createSelectionLine(),
            },
        });

        plotArea.Add(detailLayer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0,
            Children = new Drawable[]
            {
                createAccuracyBox(),
                sectionPill = new StarRatingDisplay(default, animated: true)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding(6),
                },
            },
        });

        plotArea.Add(new SectionInput(onSelectionDown, onSelectionDrag, onSelectionDragEnd, clearSelection)
        {
            RelativeSizeAxes = Axes.Both,
        });
    }

    private Box createSelectionLine() => new Box
    {
        RelativeSizeAxes = Axes.Y,
        RelativePositionAxes = Axes.X,
        Width = 1,
        Origin = Anchor.TopCentre,
        Anchor = Anchor.TopLeft,
        Colour = Color4.White,
        Alpha = 0,
    };

    /// <summary>
    /// 점 위를 덮는 흰 라운드 박스 + 검은 글씨. 각 행은 <b>양축 모두 고정 크기</b>다 - AutoSize 흐름 안에서
    /// 편측만 고정하면 즉사 패턴에 걸린다(<c>ui-patching.md</c>).
    /// </summary>
    private Drawable createAccuracyBox() => new Container
    {
        Masking = true,
        CornerRadius = 5,
        Margin = new MarginPadding(6),
        Size = new Vector2(accuracy_row_width + 16, accuracy_row_height * 3 + 10),
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Horizontal = 8, Vertical = 5 },
                Children = new Drawable[]
                {
                    createAccuracyRow("레거시", out legacyValue),
                    createAccuracyRow("lazer", out lazerValue),
                    createAccuracyRow("pp", out ppValue),
                },
            },
        },
    };

    private Drawable createAccuracyRow(string label, out OsuSpriteText value)
    {
        value = new OsuSpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Colour = Color4.Black,
            Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold, fixedWidth: true),
        };

        return new Container
        {
            Size = new Vector2(accuracy_row_width, accuracy_row_height),
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = label,
                    Colour = Color4.Black,
                    Font = OsuFont.GetFont(size: 12),
                },
                value,
            },
        };
    }

    private void onSelectionDown(float fraction)
    {
        dragStartFraction = fraction;
        dragEndFraction = fraction;

        selectionLineA.X = fraction;
        selectionLineA.Alpha = 1;
        selectionLineB.Alpha = 0;

        detailLayer.Alpha = 0;
        sunnyCts?.Cancel();
    }

    private void onSelectionDrag(float fraction)
    {
        dragEndFraction = fraction;

        selectionLineB.X = fraction;
        selectionLineB.Alpha = 1;
    }

    private void onSelectionDragEnd()
    {
        if (Math.Abs(dragEndFraction - dragStartFraction) * plotArea.DrawWidth < min_selection_pixels)
        {
            clearSelection();
            return;
        }

        applySelection(Math.Min(dragStartFraction, dragEndFraction), Math.Max(dragStartFraction, dragEndFraction));
    }

    /// <summary>드래그 없이 클릭만 하면 선도 지우고 상세 모드에 진입하지 않는다.</summary>
    private void clearSelection()
    {
        selectionLineA.Alpha = 0;
        selectionLineB.Alpha = 0;
        detailLayer.Alpha = 0;
        sunnyCts?.Cancel();

        selectionRange = null;
        practiceSection = null;
        practiceButton.Enabled.Value = false;
    }

    private void applySelection(float startFraction, float endFraction)
    {
        double startTime = firstTime + startFraction * timeSpan;
        double endTime = firstTime + endFraction * timeSpan;

        selectionRange = (startTime, endTime);

        updateAccuracyDisplay();

        detailLayer.Alpha = 1;

        updatePracticeSection(startTime, endTime);

        // sunnySR은 컬럼 필터와 무관하게 구간 전체로 잰다 - mania 난이도는 컬럼 간 상호작용이
        // 본질이라 일부 컬럼만 남긴 보면의 값은 원본과 아무 관계가 없다.
        requestSectionSunny(startTime, endTime);
    }

    /// <summary>
    /// 구간 정확도 3종을 다시 낸다. <b>활성 컬럼의 판정만 센다</b> — 컬럼 버튼을 누르면 그 자리에서 갱신된다.
    /// 노트 종류 필터는 정확도에 관여하지 않는다(전 종류를 그대로 집계한다).
    /// </summary>
    private void updateAccuracyDisplay()
    {
        if (selectionRange is not { } range)
            return;

        var events = ManiaSectionAnalysis.EventsWithin(accuracyEvents, range.Start, range.End);
        events.RemoveAll(e => !isColumnVisible(columnOf(e.HitObject)));

        var accuracy = ManiaSectionAnalysis.Accuracy(events);

        legacyValue.Text = ManiaSectionAnalysis.Format(accuracy.Legacy);
        lazerValue.Text = ManiaSectionAnalysis.Format(accuracy.Lazer);
        ppValue.Text = ManiaSectionAnalysis.Format(accuracy.Pp);
    }

    /// <summary>
    /// 연습 구간의 실제 경계를 노트 기준으로 잡는다. <b>끝은 마지막 노트의 <c>GetEndTime</c></b>이라
    /// 경계에 걸친 롱노트를 끝까지 붙잡을 수 있다.
    /// </summary>
    private void updatePracticeSection(double startTime, double endTime)
    {
        double? first = null;
        double? last = null;

        foreach (var hitObject in playableBeatmap.HitObjects)
        {
            if (hitObject.StartTime < startTime || hitObject.StartTime > endTime)
                continue;

            first ??= hitObject.StartTime;

            double end = hitObject.GetEndTime();
            if (last == null || end > last) last = end;
        }

        practiceSection = first == null ? null : (first.Value, last!.Value);
        practiceButton.Enabled.Value = practiceSection != null;
    }

    /// <summary>
    /// 잡힌 구간만 연습하는 화면으로 넘어간다. 결과창 위에 얹히므로 나가면 자연히 여기로 돌아온다.
    /// </summary>
    private void startSectionPractice()
    {
        try
        {
            if (practiceSection is not { } section)
                return;

            Drawable? current = this;

            while (current != null && current is not OsuScreen)
                current = current.Parent;

            if (current is not OsuScreen screen || !screen.IsCurrentScreen())
                return;

            screen.Push(new SectionPracticePlayerLoader(score, section.Start, section.End));
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] ManiaJudgementScatterGraph.startSectionPractice failed: {e}");
        }
    }

    /// <summary>
    /// 구간만 담은 임시 보면으로 sunnySR을 잰다. <b>짧은 맵 보정에는 전체 맵 기준 노트 수를 대입</b>하므로
    /// 구간이 짧다는 이유만으로 값이 깎이지 않는다.
    /// </summary>
    private void requestSectionSunny(double startTime, double endTime)
    {
        sunnyCts?.Cancel();
        sunnyCts = new CancellationTokenSource();

        var token = sunnyCts.Token;
        sectionPill.Alpha = 0.4f;

        Task.Run(() =>
        {
            try
            {
                var section = ManiaSectionAnalysis.BuildSection(playableBeatmap, startTime, endTime);

                double sr = section == null
                    ? 0
                    : new SunnyManiaDifficultyCalculator().Calculate(section, mods, token, fullWeightedNoteCount);

                if (token.IsCancellationRequested)
                    return;

                Schedule(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    sectionPill.Current.Value = new StarDifficulty(sr, 0);
                    sectionPill.Alpha = 1;
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] ManiaJudgementScatterGraph section sunnySR failed: {e}");
            }
        }, token);
    }

    protected override void Dispose(bool isDisposing)
    {
        sunnyCts?.Cancel();

        base.Dispose(isDisposing);
    }

    /// <summary>
    /// 컬럼 토글 버튼. 개수는 보면이 정한다 (4K면 4개). 왼쪽이 1번 컬럼.
    /// </summary>
    private ToggleButton[] createColumnButtons(OsuColour colours)
    {
        int columnCount = playableBeatmap is ManiaBeatmap mania ? mania.TotalColumns : 0;

        if (columnCount <= 0)
            return [];

        var buttons = new ToggleButton[columnCount];

        for (int i = 0; i < columnCount; i++)
        {
            buttons[i] = new ToggleButton($"{i + 1}", colours.Green, column_button_width);

            if (i == 0)
                buttons[i].Margin = new MarginPadding { Left = button_group_gap };
        }

        return buttons;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        singleButton.Active.BindValueChanged(_ => refreshPoints());
        headButton.Active.BindValueChanged(_ => refreshPoints());
        tailButton.Active.BindValueChanged(_ => refreshPoints());

        // 컬럼 필터는 점뿐 아니라 상세 모드의 정확도에도 반영된다 (sunnySR pill은 전 컬럼 기준 유지).
        foreach (var button in columnButtons)
        {
            button.Active.BindValueChanged(_ =>
            {
                refreshPoints();
                updateAccuracyDisplay();
            });
        }

        refreshPoints();
    }

    // HeadNote/TailNote가 둘 다 Note를 상속하므로 검사 순서가 곧 분류 규칙이다.
    private static NoteKind kindOf(HitObject hitObject) => hitObject switch
    {
        TailNote => NoteKind.Tail,
        HeadNote => NoteKind.Head,
        _ => NoteKind.Single,
    };

    /// <summary>0부터 시작하는 컬럼 번호. 롱헤드/롱테일 같은 중첩 오브젝트도 컬럼을 갖고 있다.</summary>
    private static int columnOf(HitObject hitObject) => (hitObject as ManiaHitObject)?.Column ?? -1;

    private bool isKindVisible(NoteKind kind) => kind switch
    {
        NoteKind.Tail => tailButton.Active.Value,
        NoteKind.Head => headButton.Active.Value,
        _ => singleButton.Active.Value,
    };

    /// <summary>컬럼을 알 수 없는 판정(mania가 아닌 경우)은 필터 대상에서 제외해 항상 보인다.</summary>
    private bool isColumnVisible(int column) =>
        column < 0 || column >= columnButtons.Length || columnButtons[column].Active.Value;

    private void refreshPoints()
    {
        if (points == null) return;

        var visible = new List<ScatterPoints.Point>(allPoints.Length);

        // 두 버튼 묶음은 교집합으로 걸린다.
        foreach (var (kind, column, point) in allPoints)
        {
            if (isKindVisible(kind) && isColumnVisible(column))
                visible.Add(point);
        }

        points.Points = visible.ToArray();
    }

    private void addWindowLines(OsuColour colours, HitWindows windows, double rate, double missWindow)
    {
        // 중앙(오차 0).
        plotArea.Add(createLine(0, Color4.White, 0.5f));
        addOffsetLabel(0, missWindow, "0");

        foreach (var result in window_results)
        {
            double window = windows.WindowFor(result) / rate;
            if (window <= 0 || window >= missWindow) continue;

            var colour = colours.ForHitResult(result);

            foreach (double signed in new[] { -window, window })
            {
                plotArea.Add(createLine((float)((signed + missWindow) / (2 * missWindow)), colour, 0.35f));

                // 320(perfect) 경계는 0선과 너무 붙어 글자가 겹치므로 선만 긋는다.
                if (result != HitResult.Perfect)
                    addOffsetLabel(signed, missWindow, $"{(signed > 0 ? "+" : "")}{Math.Round(signed)}");
            }
        }
    }

    private Drawable createLine(float relativeY, Color4 colour, float alpha) => new Box
    {
        RelativeSizeAxes = Axes.X,
        RelativePositionAxes = Axes.Y,
        Height = 1,
        Y = relativeY,
        Origin = Anchor.CentreLeft,
        Anchor = Anchor.TopLeft,
        Colour = colour,
        Alpha = alpha,
    };

    private void addOffsetLabel(double offsetMs, double missWindow, string text)
    {
        offsetLabelLayer.Add(new OsuSpriteText
        {
            RelativePositionAxes = Axes.Y,
            Y = (float)((offsetMs + missWindow) / (2 * missWindow)),
            X = axis_label_width - 4,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.CentreRight,
            Text = text,
            Font = OsuFont.GetFont(size: 10),
            Alpha = 0.7f,
        });
    }

    private void addTimeLabels(double firstTime, double lastTime)
    {
        for (int i = 0; i < time_label_count; i++)
        {
            float fraction = i / (float)(time_label_count - 1);
            double time = firstTime + (lastTime - firstTime) * fraction;

            timeLabelLayer.Add(new OsuSpriteText
            {
                RelativePositionAxes = Axes.X,
                X = fraction,
                Anchor = Anchor.TopLeft,
                Origin = i == 0 ? Anchor.TopLeft : i == time_label_count - 1 ? Anchor.TopRight : Anchor.TopCentre,
                Text = TimeSpan.FromMilliseconds(time).ToString(@"m\:ss"),
                Font = OsuFont.GetFont(size: 10),
                Alpha = 0.7f,
            });
        }
    }

    /// <summary>
    /// 플롯 전체를 덮는 투명한 입력 레이어. 그리는 것이 없지만 <c>Alpha</c>는 1이어야 한다 -
    /// 0으로 두면 <c>IsPresent</c>가 false가 되어 입력 자체가 오지 않는다(<c>ui-patching.md</c>).
    /// </summary>
    private sealed partial class SectionInput : Container
    {
        private readonly Action<float> down;
        private readonly Action<float> drag;
        private readonly Action dragEnd;
        private readonly Action click;

        public SectionInput(Action<float> down, Action<float> drag, Action dragEnd, Action click)
        {
            this.down = down;
            this.drag = drag;
            this.dragEnd = dragEnd;
            this.click = click;
        }

        private float fractionAt(Vector2 localPosition) =>
            Math.Clamp(localPosition.X / Math.Max(DrawWidth, 1), 0, 1);

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != osuTK.Input.MouseButton.Left)
                return false;

            down(fractionAt(e.MousePosition));
            return true;
        }

        // 드래그가 실제로 일어났는지는 프레임워크가 구분해준다 - 드래그가 있었으면 OnClick은 오지 않는다.
        protected override bool OnDragStart(DragStartEvent e) => e.Button == osuTK.Input.MouseButton.Left;

        protected override void OnDrag(DragEvent e) => drag(fractionAt(e.MousePosition));

        protected override void OnDragEnd(DragEndEvent e) => dragEnd();

        protected override bool OnClick(ClickEvent e)
        {
            click();
            return true;
        }
    }

    /// <summary>
    /// 구간이 잡혔을 때만 눌리는 실행 버튼. 토글이 아니라 상태를 <c>Enabled</c>가 정한다.
    /// </summary>
    private sealed partial class PracticeButton : OsuClickableContainer
    {
        private readonly Color4 accent;
        private readonly Box background;
        private readonly OsuSpriteText label;

        public PracticeButton(string text, Color4 accent)
        {
            this.accent = accent;

            // 양축 모두 고정 크기 - AutoSize 흐름 안에서 편측 고정은 피한다(ui-patching.md).
            Size = new Vector2(76, button_height);
            Masking = true;
            CornerRadius = 4;

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both },
                label = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = text,
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Enabled.BindValueChanged(e => updateState(e.NewValue), true);
        }

        private void updateState(bool enabled)
        {
            background.FadeColour(enabled ? accent : OsuColour.Gray(0.25f), 150, Easing.OutQuint);
            label.FadeColour(enabled ? Color4.White : OsuColour.Gray(0.6f), 150, Easing.OutQuint);
        }
    }

    private sealed partial class ToggleButton : OsuClickableContainer
    {
        public readonly BindableBool Active = new BindableBool(true);

        private readonly Color4 accent;
        private readonly Box background;
        private readonly OsuSpriteText label;

        public ToggleButton(string text, Color4 accent, float width = 64)
        {
            this.accent = accent;

            // 양축 모두 고정 크기 - AutoSize 흐름 안에서 편측 고정은 피한다(ui-patching.md).
            Size = new Vector2(width, button_height);
            Masking = true;
            CornerRadius = 4;

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both },
                label = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = text,
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                },
            };

            Action = Active.Toggle;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Active.BindValueChanged(e => updateState(e.NewValue), true);
        }

        private void updateState(bool active)
        {
            background.FadeColour(active ? accent : OsuColour.Gray(0.25f), 150, Easing.OutQuint);
            label.FadeColour(active ? Color4.White : OsuColour.Gray(0.6f), 150, Easing.OutQuint);
        }
    }

    /// <summary>
    /// 점 하나당 Drawable을 만들면 노트 수천 개에서 프레임마다 그만큼의 업데이트가 돌므로,
    /// <c>StrainAreaGraph</c>의 곡선과 동일하게 쿼드 배치 하나로 전부 그린다.
    /// </summary>
    private sealed partial class ScatterPoints : Drawable
    {
        public readonly record struct Point(float X, float Y, Color4 Colour);

        private const float point_size = 4f;

        /// <summary>
        /// 한 번에 담을 쿼드 수. 점 개수와 무관한 고정값이며 <c>IRenderer.MAX_QUADS</c>(10922)보다 작아야 한다.
        /// </summary>
        private const int quads_per_batch = 2048;

        private Point[] points = [];

        public Point[] Points
        {
            get => points;
            set
            {
                points = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private IShader shader = null!;

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, "FastCircle");
        }

        protected override DrawNode CreateDrawNode() => new ScatterDrawNode(this);

        private sealed class ScatterDrawNode : DrawNode
        {
            private readonly ScatterPoints source;
            private Vector2 drawSize;
            private Point[] points = [];
            private IShader shader = null!;
            private IVertexBatch<TexturedVertex2D>? batch;
            private bool drawFailed;

            public ScatterDrawNode(ScatterPoints source)
                : base(source)
            {
                this.source = source;
            }

            public override void ApplyState()
            {
                base.ApplyState();
                drawSize = source.DrawSize;
                points = source.points;
                shader = source.shader;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                if (points.Length == 0 || drawFailed) return;

                bool bound = false;

                try
                {
                    // 배치 크기는 점 개수와 무관한 고정값이다. VertexBatch.Add는 현재 버퍼가 차면
                    // 스스로 Draw()로 플러시한 뒤 이어서 담으므로, 점이 몇 개든 전부 그려지고
                    // 드로우 콜만 ceil(점 수 / quads_per_batch)개로 나뉜다.
                    //
                    // 반대로 점 개수에 맞춰 잡으면 IRenderer.MAX_QUADS(10922)를 넘는 순간 배치
                    // 생성자가 OverflowException을 던지고, batch가 null로 남아 다음 프레임에 또
                    // 던지는 무한 예외 루프가 된다 (노트가 많은 맵에서 렉/즉사의 원인이었다).
                    batch ??= renderer.CreateQuadBatch<TexturedVertex2D>(quads_per_batch, 1);

                    float radius = point_size / 2f;
                    var texRect = new Vector4(0, 0, point_size, point_size);
                    var blend = new Vector2(0.5f);
                    float alpha = DrawColourInfo.Colour.AverageColour.SRGB.A;

                    shader.Bind();
                    bound = true;

                    foreach (var p in points)
                    {
                        float cx = p.X * drawSize.X;
                        float cy = p.Y * drawSize.Y;

                        var quad = Quad.FromRectangle(new RectangleF(cx - radius, cy - radius, point_size, point_size)) * DrawInfo.Matrix;
                        var colour = new Color4(p.Colour.R, p.Colour.G, p.Colour.B, p.Colour.A * alpha);

                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.BottomLeft, TexturePosition = new Vector2(0, point_size), TextureRect = texRect, BlendRange = blend, Colour = colour });
                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.BottomRight, TexturePosition = new Vector2(point_size, point_size), TextureRect = texRect, BlendRange = blend, Colour = colour });
                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.TopRight, TexturePosition = new Vector2(point_size, 0), TextureRect = texRect, BlendRange = blend, Colour = colour });
                        batch.AddAction(new TexturedVertex2D(renderer) { Position = quad.TopLeft, TexturePosition = Vector2.Zero, TextureRect = texRect, BlendRange = blend, Colour = colour });
                    }
                }
                catch (Exception)
                {
                    // 드로우 스레드로 예외가 새어나가면 게임이 죽는다. 한 번 실패하면 다시 시도하지
                    // 않는다 - 매 프레임 재시도가 곧 예외 폭풍이 된다.
                    drawFailed = true;
                    batch?.Dispose();
                    batch = null;
                }
                finally
                {
                    if (bound)
                        shader.Unbind();
                }
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                batch?.Dispose();
            }
        }
    }
}
