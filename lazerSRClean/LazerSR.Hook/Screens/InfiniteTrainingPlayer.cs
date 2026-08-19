using System;
using System.Threading.Tasks;
using LazerSR.Hook.Training;
using osu.Framework.Allocation;
using osu.Framework.Screens;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;
using osu.Game.Users;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 무한 트레이닝 전용 게임플레이 화면.
/// <para>
/// 서버와 접촉하는 <c>SubmittingPlayer</c> 계열을 쓰지 않고 <see cref="Player"/>를 직접 상속한다 —
/// 제출 토큰 요청도, 점수 제출도, 관전 브로드캐스트(<c>SpectatorClient.BeginPlaying</c>)도 코드 경로에 없다.
/// </para>
/// </summary>
public partial class InfiniteTrainingPlayer : Player
{
    /// <summary>
    /// 첫 마디 시작 시각. 시드 비트맵의 진입용 동시치기 뒤로 1초 띄운다.
    /// (첫 <c>Update</c> 호출 시점에 의존하지 않도록 고정값으로 못박는다.)
    /// </summary>
    private const double first_measure_start_ms = InfiniteTrainingBeatmap.FIRST_CHORD_TIME_MS + 1000;

    /// <summary>측정 구간의 판정창은 사용자 설정 OD와 분리해 고정한다 — 값이 달라지면 기록끼리 비교할 수 없다.</summary>
    private const double measurement_overall_difficulty = 8.5;

    /// <summary>대기 화면에서 고른 모드. 단기 실력찾기와 무한 세션 중 하나다.</summary>
    private readonly TrainingPhase mode;

    /// <summary>그 모드의 목표 정확도(%).</summary>
    private readonly double accuracyThreshold;

    private TrainingSequencer? sequencer;
    private JudgementAggregator? aggregator;
    private ShortTermSearch? search;
    private InfiniteSession? infinite;
    private TrainingMusicPlayer? music;
    private bool sessionStarted;

    /// <summary>
    /// <c>Player</c>를 직접 상속할 때 하위 클래스가 반드시 캐시해야 하는 의존성.
    /// 스킨 HUD 레이아웃에 기본 포함된 <c>DrawableGameplayLeaderboard</c>가 이걸 nullable이 아닌
    /// <c>[Resolved]</c>로 요구하므로, 없으면 매 세션 <c>DependencyNotRegisteredException</c>이 난다.
    /// 리더보드가 없는 <c>EditorPlayer</c>와 동일하게 빈 provider를 쓴다 — 서버 조회도 표시도 없다.
    /// </summary>
    [Cached(typeof(IGameplayLeaderboardProvider))]
    private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

    /// <param name="mode">단기 실력찾기 / 무한 세션.</param>
    /// <param name="accuracyThreshold">그 모드의 목표 정확도(%). 대기 화면 설정 패널에서 온다.</param>
    public InfiniteTrainingPlayer(TrainingPhase mode, double accuracyThreshold)
        : base(new PlayerConfiguration
        {
            // 결과 화면으로 가지 않는다. 주입 때문에 ScoreProcessor.HasCompleted가 성립하지 않기도 하고,
            // 이 값이 false여야 prepareAndImportScoreAsync가 스코어를 realm에 쓰지 않는다.
            ShowResults = false,
            ShowLeaderboard = false,
        })
    {
        this.mode = mode;
        this.accuracyThreshold = accuracyThreshold;
    }

    /// <summary>
    /// 서버에 활동 상태를 알리지 않는다.
    /// 기본값은 <c>InSoloGame(비트맵, 룰셋)</c>이라 override하지 않으면 프로필에 "플레이 중"이 노출된다.
    /// </summary>
    protected override UserActivity? InitialActivity => null;

    /// <summary>
    /// 사망하지 않는다. 단기 탐색은 한계 bpm까지 올리는 게 목적이라 미스 대량 발생이 정상 동작이고,
    /// 무한 세션도 죽으면 무한이 아니게 된다.
    /// <para>
    /// <c>DrainRate = 0</c>으로는 막히지 않는다 — <c>ManiaHealthProcessor.GetHealthIncreaseFor</c>의
    /// 미스 감소량이 <c>-(DrainRate + 1) * 0.0075</c>라 0에서도 미스당 0.0075가 깎인다.
    /// </para>
    /// </summary>
    protected override bool CheckModsAllowFailure() => false;

    /// <summary>결과 화면 경로 자체를 쓰지 않는다 (<c>ShowResults = false</c>이므로 호출되지 않음).</summary>
    protected override ResultsScreen CreateResults(ScoreInfo score) =>
        throw new NotSupportedException("무한 트레이닝은 결과 화면을 사용하지 않는다.");

    /// <summary>
    /// realm(client.realm)에 스코어/리플레이를 쓰지 않는다.
    /// <c>ShowResults = false</c>가 대부분의 경로를 막지만, 실패 오버레이의 "리플레이 저장"이
    /// <c>forceImport: true</c>로 우회하므로 여기서 한 번 더 차단한다.
    /// </summary>
    protected override Task ImportScore(Score score) => Task.CompletedTask;

    protected override void Update()
    {
        base.Update();

        if (!LoadedBeatmapSuccessfully)
            return;

        // DrawableRuleset / ScoreProcessor는 비트맵 로드 이후에만 유효하므로 첫 Update에서 만든다.
        if (!sessionStarted)
        {
            sessionStarted = true;

            sequencer = new TrainingSequencer(
                DrawableRuleset.Playfield,
                GameplayState.Beatmap,
                first_measure_start_ms,
                measurement_overall_difficulty);

            // 배경음은 게임플레이 시계와 분리된 독립 채널이라 여기 붙여도 판정 타이밍에 영향이 없다.
            AddInternal(music = new TrainingMusicPlayer());

            aggregator = new JudgementAggregator(GameplayState.ScoreProcessor);

            var patterns = TrainingProfile.EnabledSubPatterns();

            if (mode == TrainingPhase.Infinite)
            {
                infinite = new InfiniteSession(sequencer, aggregator, accuracyThreshold, music);
                infinite.Start(patterns);
            }
            else
            {
                search = new ShortTermSearch(sequencer, aggregator, accuracyThreshold, music);
                search.Start(patterns);
            }
        }

        double now = GameplayClockContainer.CurrentTime;

        search?.Update(now);
        infinite?.Update(now);
        sequencer!.Update(now);
        music!.Update(now);
    }

    /// <summary>
    /// 세션 상태 정리는 <b>여기서</b> 한다. <c>Dispose</c>는 비동기 폐기 스레드에서 돌 수 있는데,
    /// 그 스레드에서 <c>Bindable</c>을 건드리면 구독 중인 위젯이 업데이트 스레드 밖에서 드로어블을
    /// 수정하게 되어 죽는다 (<c>docs\guides\ui-patching.md</c> §6).
    /// </summary>
    public override bool OnExiting(ScreenExitEvent e)
    {
        TrainingSessionState.Reset();
        music?.StopAll();

        return base.OnExiting(e);
    }

    protected override void Dispose(bool isDisposing)
    {
        // 이벤트 해제와 취소만 한다 — Bindable은 건드리지 않는다.
        search?.Dispose();
        infinite?.Dispose();
        aggregator?.Dispose();

        base.Dispose(isDisposing);
    }
}

/// <summary>
/// 무한 트레이닝용 로딩 화면. 순정 <see cref="PlayerLoader"/>와 동일하게 동작하되,
/// <c>LocalOnlyLeaderboardSkipPatch</c>가 <see cref="ILocalOnlyPlayerLoader"/>를 보고
/// 리더보드 서버 조회를 생략한다.
/// </summary>
public partial class InfiniteTrainingPlayerLoader : PlayerLoader, ILocalOnlyPlayerLoader
{
    public InfiniteTrainingPlayerLoader(Func<Player> createPlayer)
        : base(createPlayer)
    {
    }
}
