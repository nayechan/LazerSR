using System;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook.Screens;
using LazerSR.Hook.Training;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Widgets;

/// <summary>
/// 무한 트레이닝 상태 위젯. 세로로 세 컨테이너 — 상태 제목 / 다목적 / (미정).
/// <para>
/// 다목적 컨테이너는 두 모드를 가진다. <b>플레이중</b>은 이전·현재·다음·다다음 세부패턴 4행을 보여주고
/// 현재 행만 노란 테두리로 강조한다. <b>휴식</b>은 방금 확정된 값과 저장/폐기 버튼을 보여주며,
/// 사용자가 누르기 전에는 다음 세부패턴으로 넘어가지 않는다.
/// </para>
/// <para>
/// 값은 전부 <see cref="TrainingSessionState"/>에서 온다. 측정 알고리즘이 아직 없어
/// <see cref="TrainingSessionState.Active"/>가 false인 동안에는 스킨 에디터에서 크기를 잡을 수 있도록
/// 플레이스홀더를 그린다.
/// </para>
/// <para>
/// <b>선곡 화면에 배치하면 곡 등록 위젯이 된다.</b> osu!에 <c>GlobalSkinnableContainers.SongSelect</c>
/// 레이어가 있어 같은 위젯을 거기에도 놓을 수 있고, 그 자리에는 <see cref="GameplayState"/>가 없으므로
/// 그것으로 두 모드를 가른다. 선곡 모드에서는 지금 고른 난이도를 배경음 후보로 검사해
/// 펄스 bpm을 받고 <see cref="TrainingMusicStore"/>에 등록한다.
/// </para>
/// <para>
/// 레이아웃은 모든 칸이 고정 높이다 — <c>AutoSizeAxes</c> 중첩 체인을 만들지 않아
/// <c>docs\guides\ui-patching.md</c> §6의 크래시 함정을 피한다.
/// </para>
/// </summary>
public partial class TrainingStatusWidget : CompositeDrawable, ISerialisableDrawable
{
    private const float title_height = 26;
    private const float body_height = 100;
    private const float footer_height = 14;
    private const float spacing = 4;

    /// <summary>펄스 bpm 스테퍼 — ±10 / ±1 두 세트가 들어간다.</summary>
    private const float stepper_width = 184;
    private const float wide_step_width = 34;

    private const float current_row_height = 32;
    private const float other_row_height = 20;

    public bool UsesFixedAnchor { get; set; }

    [SettingSource("너비")]
    public BindableFloat WidgetWidth { get; } = new BindableFloat(360)
    {
        MinValue = 240,
        MaxValue = 640,
        Precision = 10,
    };

    private readonly Bindable<bool> active = TrainingSessionState.Active.GetBoundCopy();
    private readonly Bindable<TrainingPhase> phase = TrainingSessionState.Phase.GetBoundCopy();
    private readonly Bindable<TrainingMode> mode = TrainingSessionState.Mode.GetBoundCopy();
    private readonly Bindable<TrainingSlot?> previous = TrainingSessionState.Previous.GetBoundCopy();
    private readonly Bindable<TrainingSlot?> current = TrainingSessionState.Current.GetBoundCopy();
    private readonly Bindable<TrainingSlot?> next = TrainingSessionState.Next.GetBoundCopy();
    private readonly Bindable<TrainingSlot?> afterNext = TrainingSessionState.AfterNext.GetBoundCopy();
    private readonly Bindable<TrainingSlot?> restingResult = TrainingSessionState.RestingResult.GetBoundCopy();

    /// <summary>인게임에서만 캐시된다 — <c>null</c>이면 선곡 화면이고 곡 등록 모드로 동작한다.</summary>
    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved]
    private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

    /// <summary>사용자가 정하는 체감 펄스 bpm. <b>자동으로 판정할 수 없는 유일한 값이다.</b></summary>
    private readonly BindableDouble pulseBpm = new BindableDouble(120)
    {
        MinValue = 30,
        MaxValue = 400,
        Precision = 1,
    };

    private MusicCandidate? candidate;
    private CancellationTokenSource? analysisCts;
    private double lastAnalysedLengthMs;

    private OsuSpriteText titleText = null!;
    private Container playingView = null!;
    private Container restingView = null!;
    private SlotRow previousRow = null!;
    private SlotRow currentRow = null!;
    private SlotRow nextRow = null!;
    private SlotRow afterNextRow = null!;
    private Container pickerView = null!;
    private OsuSpriteText pickerTitleText = null!;
    private OsuSpriteText pickerResultText = null!;
    private OsuSpriteText pulseBpmText = null!;
    private OsuSpriteText pickerStatusText = null!;
    private RoundedButton pickerAddButton = null!;
    private Container restButtons = null!;
    private OsuSpriteText restingNameText = null!;
    private OsuSpriteText restingBpmText = null!;
    private StarRatingDisplay restingPill = null!;

    public TrainingStatusWidget()
    {
        Width = WidgetWidth.Value;
        Height = title_height + body_height + footer_height + spacing * 2;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, spacing),
            Children = new Drawable[]
            {
                createTitleContainer(),
                createBodyContainer(),
                createFooterContainer(),
            },
        };
    }

    private Drawable createTitleContainer() => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = title_height,
        Masking = true,
        CornerRadius = 4,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Black.Opacity(0.6f),
            },
            titleText = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = OsuFont.Torus.With(size: 16, weight: FontWeight.SemiBold),
            },
        },
    };

    private Drawable createBodyContainer() => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = body_height,
        Masking = true,
        CornerRadius = 4,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Black.Opacity(0.6f),
            },
            playingView = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 6, Vertical = 3 },
                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, other_row_height),
                        new Dimension(GridSizeMode.Absolute, current_row_height),
                        new Dimension(GridSizeMode.Absolute, other_row_height),
                        new Dimension(GridSizeMode.Absolute, other_row_height),
                    },
                    Content = new[]
                    {
                        new Drawable[] { previousRow = new SlotRow(false) },
                        new Drawable[] { currentRow = new SlotRow(true) },
                        new Drawable[] { nextRow = new SlotRow(false) },
                        new Drawable[] { afterNextRow = new SlotRow(false) },
                    },
                },
            },
            restingView = createRestingView(),
            pickerView = createPickerView(),
        },
    };

    /// <summary>선곡 화면 전용. 곡 정보 / 검사 결과 / 펄스 bpm / 추가 버튼.</summary>
    private Container createPickerView() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Alpha = 0,
        Padding = new MarginPadding { Horizontal = 8, Vertical = 3 },
        Child = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 20,
                    Child = pickerTitleText = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.Torus.With(size: 13, weight: FontWeight.SemiBold),
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 18,
                    Child = pickerResultText = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.Torus.With(size: 12),
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 24,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = OsuFont.Torus.With(size: 12),
                            Text = "펄스 BPM",
                        },
                        // 양축 고정 Container 안에 넣는다 (ui-patching.md 함정 표).
                        new Container
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Size = new Vector2(stepper_width, 22),
                            Children = new Drawable[]
                            {
                                new StepButton("−10", () => pulseBpm.Value -= 10)
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Width = wide_step_width,
                                },
                                new StepButton("−", () => pulseBpm.Value -= 1)
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Margin = new MarginPadding { Left = wide_step_width + 4 },
                                },
                                pulseBpmText = new OsuSpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Font = OsuFont.Torus.With(size: 13, fixedWidth: true),
                                },
                                new StepButton("+", () => pulseBpm.Value += 1)
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    Margin = new MarginPadding { Right = wide_step_width + 4 },
                                },
                                new StepButton("+10", () => pulseBpm.Value += 10)
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    Width = wide_step_width,
                                },
                            },
                        },
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 28,
                    Children = new Drawable[]
                    {
                        pickerAddButton = new RoundedButton
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Width = 96,
                            Height = 24,
                            Text = "곡 추가",
                            Action = addCurrentSong,
                        },
                        pickerStatusText = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Margin = new MarginPadding { Left = 104 },
                            Font = OsuFont.Torus.With(size: 12),
                        },
                    },
                },
            },
        },
    };

    private Container createRestingView() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Alpha = 0,
        Padding = new MarginPadding { Horizontal = 10, Vertical = 6 },
        Child = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 2),
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 20,
                    Child = restingNameText = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.Torus.With(size: 14, weight: FontWeight.SemiBold),
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 24,
                    Children = new Drawable[]
                    {
                        restingBpmText = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = OsuFont.Torus.With(size: 14, fixedWidth: true),
                        },
                        restingPill = new StarRatingDisplay(default, StarRatingDisplaySize.Small)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Margin = new MarginPadding { Left = 90 },
                        },
                    },
                },
                restButtons = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 30,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new RoundedButton
                            {
                                Width = 96,
                                Height = 26,
                                Text = "저장",
                                Action = () => TrainingSessionState.SubmitRestDecision(true),
                            },
                            new RoundedButton
                            {
                                Width = 96,
                                Height = 26,
                                Text = "폐기",
                                Action = () => TrainingSessionState.SubmitRestDecision(false),
                            },
                        },
                    },
                },
            },
        },
    };

    /// <summary>역할 미정. 자리만 잡아둔다.</summary>
    private Drawable createFooterContainer() => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = footer_height,
        Masking = true,
        CornerRadius = 4,
        Child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = Color4.White.Opacity(0.06f),
        },
    };

    protected override void LoadComplete()
    {
        base.LoadComplete();

        WidgetWidth.BindValueChanged(w => Width = w.NewValue, true);

        if (pickerMode)
        {
            beatmap.BindValueChanged(_ =>
            {
                lastAnalysedLengthMs = double.NegativeInfinity;
                requestAnalysis();
            }, true);

            pulseBpm.BindValueChanged(_ => updatePicker());
            TrainingMusicStore.Changed += updatePicker;
        }

        active.BindValueChanged(_ => refresh());
        phase.BindValueChanged(_ => refresh());
        mode.BindValueChanged(_ => refresh());
        previous.BindValueChanged(_ => refresh());
        current.BindValueChanged(_ => refresh());
        next.BindValueChanged(_ => refresh());
        afterNext.BindValueChanged(_ => refresh());
        restingResult.BindValueChanged(_ => refresh());

        refresh();
    }

    /// <summary>인게임이 아니면(= <see cref="GameplayState"/>가 없으면) 선곡 화면이다.</summary>
    private bool pickerMode => gameplayState == null;

    private void refresh()
    {
        if (pickerMode)
        {
            titleText.Text = "무한 트레이닝 곡 추가";
            playingView.Alpha = 0;
            restingView.Alpha = 0;
            pickerView.Alpha = 1;

            updatePicker();
            return;
        }

        pickerView.Alpha = 0;

        bool live = active.Value;

        titleText.Text = titleFor(live ? phase.Value : TrainingPhase.ShortTerm);

        bool resting = live && mode.Value == TrainingMode.Resting;

        playingView.Alpha = resting ? 0 : 1;
        restingView.Alpha = resting ? 1 : 0;

        if (resting)
        {
            var result = restingResult.Value;

            // 무한은 bpm이 변하지 않으므로 확정할 것이 없다 — 저장/폐기 버튼 없이 다음 패턴만 예고한다.
            bool infinite = phase.Value == TrainingPhase.Infinite;

            restButtons.Alpha = infinite ? 0 : 1;

            string label = infinite ? "휴식 — 다음" : "측정 완료";

            restingNameText.Text = result == null ? "휴식" : $"{label} — {result.DisplayName}";
            restingBpmText.Text = result == null ? string.Empty : $"{result.Bpm:0} BPM";
            restingPill.Current.Value = new StarDifficulty(result?.StarRating ?? 0, 0);
            return;
        }

        previousRow.SetSlot(live ? previous.Value : placeholder_previous);
        currentRow.SetSlot(live ? current.Value : placeholder_current);
        nextRow.SetSlot(live ? next.Value : placeholder_next);
        afterNextRow.SetSlot(live ? afterNext.Value : null);
    }

    // ── 선곡 모드 ────────────────────────────────────────────────────────────

    /// <summary>선택된 맵의 오디오 길이. 아직 로드 전이면 0이다 (선곡 화면은 미리듣기 때문에 곧 채워진다).</summary>
    private double currentTrackLength()
    {
        var working = beatmap.Value;

        return working.TrackLoaded ? working.Track.Length : 0;
    }

    protected override void Update()
    {
        base.Update();

        if (!pickerMode)
            return;

        // 오디오 길이는 미리듣기 트랙이 로드된 뒤에야 읽히므로, 값이 생기면 그때 다시 검사한다.
        double length = currentTrackLength();

        if (Math.Abs(length - lastAnalysedLengthMs) > 1)
            requestAnalysis();
    }

    /// <summary>보면 파싱이 들어가므로 백그라운드에서 돌리고 결과만 업데이트 스레드로 되돌린다.</summary>
    private void requestAnalysis()
    {
        analysisCts?.Cancel();
        analysisCts = new CancellationTokenSource();

        var token = analysisCts.Token;
        var working = beatmap.Value;
        double length = currentTrackLength();

        lastAnalysedLengthMs = length;
        candidate = null;
        updatePicker();

        Task.Run(() =>
        {
            var result = BeatmapMusicAnalysis.Analyse(working, length);

            if (token.IsCancellationRequested)
                return;

            Schedule(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                candidate = result;

                // 펄스 bpm은 추정값으로 채워두고 사용자가 고친다.
                if (result.Track != null)
                    pulseBpm.Value = result.Track.BaseBpm;

                updatePicker();
            });
        }, token);
    }

    private void updatePicker()
    {
        if (pickerView == null)
            return;

        var metadata = beatmap.Value.Metadata;

        pickerTitleText.Text = $"{metadata.Artist} - {metadata.Title}";
        pulseBpmText.Text = $"{pulseBpm.Value:0}";
        pickerStatusText.Text = $"등록 {TrainingMusicStore.Songs.Count}곡";

        if (candidate == null)
        {
            pickerResultText.Text = string.Empty;
            pickerAddButton.Enabled.Value = false;
            return;
        }

        bool usable = candidate.Suitable;

        pickerResultText.Text = usable ? "사용 가능" : "사용 불가";
        pickerResultText.Colour = usable ? Color4.LightGreen : Color4.Salmon;

        // 이미 있는 곡은 "사용 불가"가 아니다 — 판정은 그대로 두고 버튼만 잠근다.
        bool duplicate = usable && TrainingMusicStore.Contains(candidate.Track!.AudioHash);

        if (duplicate)
            pickerStatusText.Text = "이미 등록됨";

        pickerAddButton.Enabled.Value = usable && !duplicate;
    }

    /// <summary>펄스 bpm만 사용자가 정한 값으로 갈아끼워 등록한다.</summary>
    private void addCurrentSong()
    {
        var track = candidate?.Track;

        if (track == null)
            return;

        TrainingMusicStore.Add(track with { BaseBpm = pulseBpm.Value });
        updatePicker();
    }

    protected override void Dispose(bool isDisposing)
    {
        // static 이벤트에 델리게이트가 남으면 위젯이 GC되지 않는다.
        TrainingMusicStore.Changed -= updatePicker;
        analysisCts?.Cancel();

        base.Dispose(isDisposing);
    }

    private static string titleFor(TrainingPhase value)
    {
        switch (value)
        {
            case TrainingPhase.Infinite:
                return "무한 트레이닝";

            default:
                return "단기 실력찾기";
        }
    }

    // 스킨 에디터 배치용. 세션이 없을 때만 그려진다 — 다다음 행은 비워서 공백 표시도 같이 보이게 한다.
    private static readonly TrainingSlot placeholder_previous = new TrainingSlot("24코드잭", 118, 5.12);
    private static readonly TrainingSlot placeholder_current = new TrainingSlot("24코드잭", 124, 5.41);
    private static readonly TrainingSlot placeholder_next = new TrainingSlot("24코드잭", 120, 5.20);

    /// <summary>세부패턴 한 행 — 이름 / bpm / sunnySR pill. 슬롯이 null이면 전부 비운다.</summary>
    private partial class SlotRow : CompositeDrawable
    {
        private const float bpm_width = 76;
        private const float pill_width = 72;

        private readonly bool highlighted;

        private OsuSpriteText nameText = null!;
        private OsuSpriteText bpmText = null!;
        private StarRatingDisplay pill = null!;
        private Container content = null!;

        public SlotRow(bool highlighted)
        {
            this.highlighted = highlighted;

            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            float fontSize = highlighted ? 15 : 12;

            InternalChild = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 4,
                BorderThickness = highlighted ? 2 : 0,
                BorderColour = Color4.Yellow,
                Children = new Drawable[]
                {
                    // 테두리만 그리기 위한 투명 배경. AlwaysPresent가 없으면 테두리도 함께 사라진다.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 6 },
                        ColumnDimensions = new[]
                        {
                            new Dimension(),
                            new Dimension(GridSizeMode.Absolute, bpm_width),
                            new Dimension(GridSizeMode.Absolute, pill_width),
                        },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = nameText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = OsuFont.Torus.With(size: fontSize, weight: highlighted ? FontWeight.SemiBold : FontWeight.Regular),
                                    },
                                },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = bpmText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = OsuFont.Torus.With(size: fontSize, fixedWidth: true),
                                    },
                                },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = pill = new StarRatingDisplay(default, StarRatingDisplaySize.Small)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Scale = new Vector2(highlighted ? 1f : 0.85f),
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        public void SetSlot(TrainingSlot? slot)
        {
            // load() 이전에 호출될 수 있다 (LoadComplete에서 refresh를 한 번 돌리므로 실제로는 이후지만,
            // 스킨 에디터에서 위젯이 재구성될 때를 대비해 방어한다).
            if (content == null)
                return;

            if (slot == null)
            {
                nameText.Text = string.Empty;
                bpmText.Text = string.Empty;
                pill.Alpha = 0;
                return;
            }

            nameText.Text = slot.DisplayName;
            bpmText.Text = $"{slot.Bpm:0}";
            pill.Alpha = 1;
            pill.Current.Value = new StarDifficulty(slot.StarRating, 0);
        }
    }
}
