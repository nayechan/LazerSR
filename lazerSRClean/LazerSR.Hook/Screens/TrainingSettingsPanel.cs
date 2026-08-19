using System;
using LazerSR.Hook.Training;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 우상단 무한 트레이닝 설정 패널.
/// <para>
/// OD만 실제로 반영된다 — 시드 비트맵의 <c>OverallDifficulty</c>로 전달되고, 주입되는 노트가
/// <c>ApplyDefaults</c>를 거치며 그 값으로 판정창을 잡는다. 측정 단계가 생기면 그 구간의 노트만
/// OD 8.5로 따로 주입하게 된다.
/// </para>
/// <para>
/// 목표 정확도는 <b>단기/무한 두 가지뿐</b>이고 패턴별로 다르지 않다 — 사용자가 여기서 직접 입력한
/// 값이 <c>ShortTermSearch</c>로 그대로 내려간다. (무한 세션 몫은 아직 없다.)
/// </para>
/// <para>
/// HP는 노출하지 않는다 — <see cref="InfiniteTrainingPlayer"/>가 사망 자체를 막으므로 의미가 없다.
/// </para>
/// </summary>
public partial class TrainingSettingsPanel : CompositeDrawable
{
    /// <summary>측정 단계에서 쓰기로 정한 고정 OD. 지금은 기본값 역할만 한다.</summary>
    public const double MEASUREMENT_OVERALL_DIFFICULTY = 8.5;

    /// <summary>단기 목표 정확도 기본값(%).</summary>
    private const double default_short_term_accuracy = 96;

    /// <summary>무한 목표 정확도 기본값(%). 세트를 끊는 기준이라 단기와 별개로 둔다.</summary>
    private const double default_infinite_accuracy = 96;

    /// <summary>−/+ 한 번에 움직이는 정확도(%).</summary>
    private const double accuracy_step = 0.1;

    private const float row_height = 34;
    private const float stepper_width = 116;

    public readonly BindableDouble OverallDifficulty = new BindableDouble(MEASUREMENT_OVERALL_DIFFICULTY)
    {
        MinValue = 0,
        MaxValue = 10,
        Precision = 0.1,
    };

    /// <summary>단기 실력찾기 모드. <see cref="RunInfinite"/>와 배타다.</summary>
    public readonly BindableBool RunShortTermMeasurement = new BindableBool(true);

    /// <summary>무한 세션 모드. <see cref="RunShortTermMeasurement"/>와 배타다.</summary>
    public readonly BindableBool RunInfinite = new BindableBool();

    /// <summary>지금 고른 모드. 둘 다 꺼져 있으면 <c>null</c>이고 시작할 수 없다.</summary>
    public TrainingPhase? SelectedMode
    {
        get
        {
            if (RunShortTermMeasurement.Value)
                return TrainingPhase.ShortTerm;

            return RunInfinite.Value ? TrainingPhase.Infinite : null;
        }
    }

    /// <summary>모드 선택이 바뀌었을 때. 시작 버튼 활성 여부가 여기 걸린다.</summary>
    public event Action? ModeChanged;

    /// <summary>
    /// 단기 측정의 정확도 임계(%). 지속상승·휴식상승이 이 값 하나를 같이 쓴다.
    /// <c>Precision</c>이 0.1이라 −/+ 한 번이 곧 한 스텝이고, 범위 밖으로는 나가지 않는다.
    /// </summary>
    public readonly BindableDouble ShortTermAccuracyThreshold = new BindableDouble(default_short_term_accuracy)
    {
        MinValue = 50,
        MaxValue = 100,
        Precision = 0.1,
    };

    /// <summary>무한 세트를 끊는 목표 정확도(%). 단기와 값이 따로 논다.</summary>
    public readonly BindableDouble InfiniteAccuracyThreshold = new BindableDouble(default_infinite_accuracy)
    {
        MinValue = 50,
        MaxValue = 100,
        Precision = 0.1,
    };

    private OsuSpriteText odValueText = null!;
    private OsuSpriteText thresholdText = null!;
    private OsuSpriteText infiniteThresholdText = null!;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    public TrainingSettingsPanel()
    {
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 10;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colourProvider.Background4,
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 8),
                Padding = new MarginPadding { Horizontal = 16, Vertical = 14 },
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Font = OsuFont.TorusAlternate.With(size: 20),
                        Text = "설정",
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = row_height,
                        Child = new OsuCheckbox
                        {
                            RelativeSizeAxes = Axes.X,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            LabelText = "단기 실력찾기 실행",
                            Current = { BindTarget = RunShortTermMeasurement },
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = row_height,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.Default.With(size: 15),
                                Text = "단기 목표 정확도",
                            },
                            // 양축 모두 고정한 Container로 감싼다 — 편측 고정 크기 + Anchor 조합이
                            // 깊은 AutoSize 체인에서 즉사를 부른 전례가 있다 (ui-patching.md 함정 표).
                            new Container
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Size = new osuTK.Vector2(stepper_width, 22),
                                Children = new Drawable[]
                                {
                                    new StepButton("−", () => stepThreshold(-1))
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    },
                                    thresholdText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Font = OsuFont.Default.With(size: 14, fixedWidth: true),
                                        Colour = colourProvider.Content2,
                                    },
                                    new StepButton("+", () => stepThreshold(1))
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                    },
                                },
                            },
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = row_height,
                        Child = new OsuCheckbox
                        {
                            RelativeSizeAxes = Axes.X,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            LabelText = "무한 세션 실행",
                            Current = { BindTarget = RunInfinite },
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = row_height,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.Default.With(size: 15),
                                Text = "무한 목표 정확도",
                            },
                            new Container
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Size = new osuTK.Vector2(stepper_width, 22),
                                Children = new Drawable[]
                                {
                                    new StepButton("−", () => InfiniteAccuracyThreshold.Value -= accuracy_step)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    },
                                    infiniteThresholdText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Font = OsuFont.Default.With(size: 14, fixedWidth: true),
                                        Colour = colourProvider.Content2,
                                    },
                                    new StepButton("+", () => InfiniteAccuracyThreshold.Value += accuracy_step)
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                    },
                                },
                            },
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = row_height,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.Default.With(size: 15),
                                Text = "OD",
                            },
                            odValueText = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Margin = new MarginPadding { Left = 34 },
                                Font = OsuFont.Default.With(size: 15, fixedWidth: true),
                                Colour = colourProvider.Content2,
                            },
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 24,
                        Child = new RoundedSliderBar<double>
                        {
                            RelativeSizeAxes = Axes.X,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Current = OverallDifficulty,
                        },
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        OverallDifficulty.BindValueChanged(od => odValueText.Text = od.NewValue.ToString("0.0"), true);

        ShortTermAccuracyThreshold.BindValueChanged(v => thresholdText.Text = $"{v.NewValue:0.0}%", true);
        InfiniteAccuracyThreshold.BindValueChanged(v => infiniteThresholdText.Text = $"{v.NewValue:0.0}%", true);

        // 두 모드는 배타다 — 하나를 켜면 다른 하나가 꺼진다. 둘 다 끄는 것은 허용하고, 그때는 시작할 수 없다.
        RunShortTermMeasurement.BindValueChanged(e =>
        {
            if (e.NewValue)
                RunInfinite.Value = false;

            ModeChanged?.Invoke();
        });

        RunInfinite.BindValueChanged(e =>
        {
            if (e.NewValue)
                RunShortTermMeasurement.Value = false;

            ModeChanged?.Invoke();
        });
    }

    /// <summary>범위 밖으로 나가는 것과 자릿수 정리는 <see cref="ShortTermAccuracyThreshold"/>가 알아서 한다.</summary>
    private void stepThreshold(int direction) =>
        ShortTermAccuracyThreshold.Value += direction * accuracy_step;
}
