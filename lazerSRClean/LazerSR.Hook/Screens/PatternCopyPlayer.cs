using System;
using System.Threading.Tasks;
using LazerSR.Hook.Input;
using LazerSR.Hook.PatternCopy;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;
using osu.Game.Users;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 패턴 복제 모드의 게임플레이 화면 — 지금은 <b>진입용 동시치기 하나 말고는 비어 있는 맵</b>이다.
/// <para>
/// newScreen(<c>C:\dev\ScreenEditor\newScreen</c>)이 대상 게임 화면에서 감지한 노트를 실시간으로
/// 여기에 주입하는 것이 최종 목표이고, 이 화면은 그것을 기다리는 "스케치북" 역할이다.
/// 대상 게임의 곡이 끝나도 주입만 멈추고 이 화면은 유지된다 — 사용자가 ESC로 나가야 끝난다.
/// </para>
/// <para>
/// 서버 격리는 무한 트레이닝(<c>InfiniteTrainingPlayer</c>)과 동일하다 —
/// <c>SubmittingPlayer</c> 계열을 쓰지 않고 <see cref="Player"/>를 직접 상속하므로
/// 제출 토큰 요청도, 점수 제출도, 관전 브로드캐스트도 코드 경로에 없다.
/// 차단 항목 전체는 <c>docs\guides\safety.md</c>의 표를 따른다.
/// </para>
/// </summary>
public partial class PatternCopyPlayer : Player
{
    /// <summary>
    /// <c>Player</c>를 직접 상속할 때 하위 클래스가 반드시 캐시해야 하는 의존성.
    /// 스킨 HUD 레이아웃에 기본 포함된 <c>DrawableGameplayLeaderboard</c>가 이걸 nullable이 아닌
    /// <c>[Resolved]</c>로 요구하므로, 없으면 매 세션 <c>DependencyNotRegisteredException</c>이 난다.
    /// </summary>
    [Cached(typeof(IGameplayLeaderboardProvider))]
    private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

    /// <summary>입력 릴레이 등록에 필요하다 (<see cref="RawKeyRelay"/>).</summary>
    [Resolved]
    private GameHost host { get; set; } = null!;

    /// <summary>newScreen이 보내온 노트를 실제로 놓는다. 비트맵 로드 이후에만 만들 수 있다.</summary>
    private PatternCopyInjector? injector;

    public PatternCopyPlayer()
        : base(new PlayerConfiguration
        {
            // 결과 화면으로 가지 않는다. 이 값이 false여야 prepareAndImportScoreAsync가 스코어를 realm에 쓰지 않는다.
            ShowResults = false,
            ShowLeaderboard = false,
        })
    {
    }

    /// <summary>
    /// 서버에 활동 상태를 알리지 않는다.
    /// 기본값은 <c>InSoloGame(비트맵, 룰셋)</c>이라 override하지 않으면 프로필에 "플레이 중"이 노출된다.
    /// </summary>
    protected override UserActivity? InitialActivity => null;

    /// <summary>
    /// 사망하지 않는다 — 대상 게임을 따라가는 세션이라 미스가 나도 끊기면 안 된다.
    /// <para>
    /// <c>DrainRate = 0</c>으로는 막히지 않는다 — <c>ManiaHealthProcessor.GetHealthIncreaseFor</c>의
    /// 미스 감소량이 <c>-(DrainRate + 1) * 0.0075</c>라 0에서도 미스당 0.0075가 깎인다.
    /// </para>
    /// </summary>
    protected override bool CheckModsAllowFailure() => false;

    /// <summary>
    /// 창 포커스를 잃어도 일시정지하지 않는다.
    /// <para>
    /// 이 모드는 <b>사용자가 대상 게임을 조작하는 동안</b> osu!가 계속 돌아야 성립한다.
    /// 대상 게임으로 창을 옮기는 순간 osu!는 반드시 포커스를 잃으므로, 기본 동작(<c>true</c>)이면
    /// 곡을 시작하기도 전에 매번 일시정지된다.
    /// </para>
    /// <para>
    /// ESC로 직접 일시정지·종료하는 경로는 그대로 남는다 — 이 모드를 끝내는 수단이 그것뿐이다.
    /// </para>
    /// </summary>
    protected override bool PauseOnFocusLost => false;

    /// <summary>결과 화면 경로 자체를 쓰지 않는다 (<c>ShowResults = false</c>이므로 호출되지 않음).</summary>
    protected override ResultsScreen CreateResults(ScoreInfo score) =>
        throw new NotSupportedException("패턴 복제 모드는 결과 화면을 사용하지 않는다.");

    /// <summary>
    /// realm(client.realm)에 스코어/리플레이를 쓰지 않는다.
    /// <c>ShowResults = false</c>가 대부분의 경로를 막지만, 실패 오버레이의 "리플레이 저장"이
    /// <c>forceImport: true</c>로 우회하므로 여기서 한 번 더 차단한다.
    /// </summary>
    protected override Task ImportScore(Score score) => Task.CompletedTask;

    /// <summary>
    /// 포커스가 없어도 키 입력을 받게 한다 — 이 모드에서는 대상 게임이 포커스를 쥐고 있다.
    /// <para>
    /// <b>프레임 관련 설정은 건드리지 않는다.</b> 한때 비활성 창 프레임 저하를 막으려고
    /// <c>GameHost.MaximumInactiveHz</c>를 올렸는데, 그 값은 "저하 기능의 스위치"가 아니라
    /// <b>비활성일 때의 상한값 자체</b>라서 상한이 통째로 사라졌다. VSync를 쓰면
    /// <c>MaximumDrawHz</c>도 <c>int.MaxValue</c>(제한은 vsync가 담당)인데 가려진 창은 vsync에
    /// 물리지 않아, 결국 아무것도 제한하지 않는 루프가 되어 프레임이 폭주하다 렉으로 끊기길 반복했다.
    /// 실측상 비포커스에서도 프레임이 떨어지지 않으므로 애초에 막을 문제가 없었다.
    /// </para>
    /// </summary>
    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        RawKeyRelay.Start(host);

        // 화면이 떠 있는 동안에만 명령을 받는다 — 아니면 다음 진입 때 과거 노트가 쏟아진다.
        PatternCopyBridge.Clear();
        PatternCopyBridge.Active = true;
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        PatternCopyBridge.Active = false;
        PatternCopyBridge.Clear();
        RawKeyRelay.Stop();

        return base.OnExiting(e);
    }

    protected override void Update()
    {
        base.Update();

        if (!LoadedBeatmapSuccessfully)
            return;

        // DrawableRuleset은 비트맵 로드 이후에만 유효하다.
        injector ??= new PatternCopyInjector(DrawableRuleset.Playfield, GameplayState.Beatmap);

        // 파이프는 백그라운드 스레드에서 읽히므로, 실제 주입은 업데이트 스레드인 여기서 한다.
        double now = GameplayClockContainer.CurrentTime;

        while (PatternCopyBridge.TryDequeue(out var command))
            injector.Process(command, now);
    }
}

/// <summary>
/// 패턴 복제 모드용 로딩 화면. 순정 <see cref="PlayerLoader"/>와 동일하게 동작하되,
/// <c>LocalOnlyLeaderboardSkipPatch</c>가 <see cref="ILocalOnlyPlayerLoader"/>를 보고
/// 리더보드 서버 조회를 생략한다.
/// </summary>
public partial class PatternCopyPlayerLoader : PlayerLoader, ILocalOnlyPlayerLoader
{
    public PatternCopyPlayerLoader()
        : base(() => new PatternCopyPlayer())
    {
    }
}
