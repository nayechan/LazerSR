using System;
using LazerSR.Hook.Training;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Game.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Menu;
using osu.Game.Screens.OnlinePlay;
using osu.Game.Screens.OnlinePlay.Components;
using osu.Game.Screens.OnlinePlay.Match.Components;
using osu.Game.Users;

namespace LazerSR.Hook.Screens;

/// <summary>
/// '무한 트레이닝' 대기 화면.
/// 배경 구성은 멀티플레이 로비(<see cref="OnlinePlayScreen"/>)와 동일하게 맞춘다 —
/// 검은 배경 스크린 + plum 색조의 웨이브 컨테이너.
/// </summary>
public partial class InfiniteTrainingScreen : OsuScreen
{
    /// <summary>멀티플레이 계열 화면의 관행대로 plum 색조를 자식들에게 제공한다.</summary>
    [Cached]
    private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Plum);

    /// <summary>
    /// 서버에 활동 상태를 알리지 않는다 (메인 메뉴 / <c>PlayerLoader</c> / 스킨 에디터와 동일).
    /// 기본값도 null이지만, 이 화면의 로컬 전용 성격이 실수로 깨지지 않도록 명시한다.
    /// </summary>
    protected override UserActivity? InitialActivity => null;

    protected override BackgroundScreen CreateBackground() => new InfiniteTrainingBackgroundScreen();

    private OnlinePlayScreenWaveContainer waves = null!;
    private TrainingSettingsPanel settings = null!;
    private PurpleRoundedButton startButton = null!;

    [Resolved]
    private RulesetStore rulesets { get; set; } = null!;

    [Resolved]
    private AudioManager audio { get; set; } = null!;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    public InfiniteTrainingScreen()
    {
        RelativeSizeAxes = Axes.Both;
        Padding = new MarginPadding { Horizontal = -HORIZONTAL_OVERFLOW_PADDING };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = waves = new OnlinePlayScreenWaveContainer
        {
            RelativeSizeAxes = Axes.Both,
            // 좌: 패턴 목록 / 우상: 설정 / 우하: 시작 버튼.
            // 모든 칸을 GridContainer로 나눠 AutoSize 중첩 체인을 만들지 않는다.
            Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding
                {
                    Horizontal = HORIZONTAL_OVERFLOW_PADDING + 40,
                    Vertical = 40,
                },
                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ColumnDimensions = new[]
                    {
                        new Dimension(),
                        new Dimension(GridSizeMode.Relative, 0.36f),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new PatternListPanel(),
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Left = 20 },
                                // 위: 설정 / 가운데: 등록곡 목록(남는 높이 전부) / 아래: 시작 버튼.
                                Child = new GridContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    RowDimensions = new[]
                                    {
                                        new Dimension(GridSizeMode.Absolute, 336),
                                        new Dimension(),
                                        new Dimension(GridSizeMode.Absolute, 70),
                                    },
                                    Content = new[]
                                    {
                                        new Drawable[] { settings = new TrainingSettingsPanel() },
                                        new Drawable[]
                                        {
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Padding = new MarginPadding { Vertical = 12 },
                                                Child = new SongListPanel(),
                                            },
                                        },
                                        new Drawable[]
                                        {
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Child = startButton = new PurpleRoundedButton
                                                {
                                                    Anchor = Anchor.BottomRight,
                                                    Origin = Anchor.BottomRight,
                                                    Width = 260,
                                                    Height = 50,
                                                    Text = "무한 트레이닝 시작",
                                                    Action = startTraining,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        TrainingProfile.Changed += updateStartButton;
        settings.ModeChanged += updateStartButton;
        updateStartButton();
    }

    /// <summary>패턴이 하나도 없거나 모드를 고르지 않았으면 시작할 수 없다.</summary>
    private void updateStartButton()
    {
        bool anyPattern = TrainingProfile.EnabledSubPatterns().Count > 0;
        bool anyMode = settings.SelectedMode != null;

        startButton.Enabled.Value = anyPattern && anyMode;

        if (!anyPattern)
            startButton.Text = "패턴을 하나 이상 선택하세요";
        else if (!anyMode)
            startButton.Text = "모드를 선택하세요";
        else
            startButton.Text = "무한 트레이닝 시작";
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        // 배경음은 원본 비트맵을 참조만 하므로 맵이 지워지면 곡도 없어진다. 들어올 때 한 번 정리한다.
        MusicLibrary.PruneMissing(beatmaps);

        this.FadeIn();
        waves.Show();
    }

    /// <summary>
    /// 코드로 만든 시드 비트맵으로 mania 세션에 진입한다.
    /// 종료/사망 시에는 <see cref="PlayerLoader"/>가 이 화면 위에 얹혀 있으므로 자연히 여기로 돌아온다.
    /// </summary>
    private void startTraining()
    {
        try
        {
            if (!this.IsCurrentScreen())
                return;

            RulesetInfo? mania = rulesets.GetRuleset(@"mania");

            if (mania == null)
            {
                HookLog.Write("[LazerSR] InfiniteTrainingScreen: mania ruleset not found.");
                return;
            }

            Beatmap.Value = InfiniteTrainingBeatmap.Create(mania, audio, settings.OverallDifficulty.Value);
            Ruleset.Value = mania;
            Mods.Value = Array.Empty<Mod>();

            var mode = settings.SelectedMode;

            if (mode == null)
                return;

            // 임계는 시작 시점의 값으로 못박는다 — 세션 도중 대기 화면 입력이 바뀌어도 기준이 흔들리지 않는다.
            double threshold = mode == TrainingPhase.Infinite
                ? settings.InfiniteAccuracyThreshold.Value
                : settings.ShortTermAccuracyThreshold.Value;

            this.Push(new InfiniteTrainingPlayerLoader(() => new InfiniteTrainingPlayer(mode.Value, threshold)));
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] InfiniteTrainingScreen.startTraining failed: {ex}");
        }
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);

        this.FadeIn(250);
        this.ScaleTo(1, 250, Easing.OutSine);
    }

    public override void OnSuspending(ScreenTransitionEvent e)
    {
        base.OnSuspending(e);

        this.ScaleTo(1.1f, 250, Easing.InSine);
        this.FadeOut(250);
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        waves.Hide();
        this.Delay(WaveContainer.DISAPPEAR_DURATION).FadeOut();

        return base.OnExiting(e);
    }

    protected override void LogoExiting(OsuLogo logo)
    {
        base.LogoExiting(logo);

        // 웨이브 전환이 기본 로고 페이드보다 길어서 맞춰준다 (OnlinePlayScreen과 동일).
        logo.Delay(WaveContainer.DISAPPEAR_DURATION / 2).FadeOut();
    }

    /// <summary>
    /// 멀티플레이 로비가 실제로 보여주는 배경. <see cref="OnlinePlayBackgroundScreen"/>이
    /// 기본 비트맵 배경을 블러(10) + 세로 그라데이션 틴트(0.1 → 0.4)로 처리해준다.
    /// 로비와 달리 방 선택이 없으므로 <c>PlaylistItem</c>은 계속 null이고, 항상 기본 배경이 쓰인다.
    /// </summary>
    private partial class InfiniteTrainingBackgroundScreen : OnlinePlayBackgroundScreen
    {
    }

    protected override void Dispose(bool isDisposing)
    {
        TrainingProfile.Changed -= updateStartButton;

        if (settings.IsNotNull())
            settings.ModeChanged -= updateStartButton;

        base.Dispose(isDisposing);
    }
}
