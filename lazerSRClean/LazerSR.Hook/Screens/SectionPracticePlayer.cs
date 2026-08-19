using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;
using osu.Game.Users;
using osu.Game.Utils;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 결과창 산점도에서 잡은 구간만 연습하는 게임플레이 화면.
/// <para>
/// 플레이하는 것은 <see cref="SectionPracticeBeatmap"/> — 원본 비트맵에서 구간 밖 노트만 걷어낸 사본이다.
/// osu!가 보기에는 그냥 짧은 맵이므로 완료 판정·점수·정확도 집계가 전부 평소 경로 그대로 맞는다.
/// 이 클래스가 하는 일은 <b>시작 지점을 구간 앞으로 당기는 것</b>과 <b>끝나면 나가는 것</b> 둘뿐이다.
/// </para>
/// <para>
/// 서버와 접촉하는 <c>SubmittingPlayer</c> 계열을 쓰지 않고 <see cref="Player"/>를 직접 상속한다.
/// 활동 상태도 보내지 않으므로 서버에는 결과창에 머물러 있는 것과 구별되지 않는다.
/// </para>
/// </summary>
public partial class SectionPracticePlayer : Player
{
    /// <summary>구간 첫 노트 앞에 두는 대비 시간. <b>체감 기준</b>이라 배속을 곱해 맵 시간으로 환산한다.</summary>
    private const double lead_in_ms = 1500;

    /// <summary>구간이 끝난 뒤 화면을 나가기까지의 여유. 마지막 노트의 판정을 볼 시간을 준다.</summary>
    private const double exit_delay_ms = 1000;

    private readonly double sectionStartTime;

    private bool exitQueued;

    /// <summary>
    /// <c>Player</c>를 직접 상속할 때 하위 클래스가 반드시 캐시해야 하는 의존성
    /// (<c>architecture.md</c> §7). 리더보드 조회도 표시도 없다.
    /// </summary>
    [Cached(typeof(IGameplayLeaderboardProvider))]
    private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

    public SectionPracticePlayer(double sectionStartTime)
        : base(new PlayerConfiguration
        {
            // 결과 화면으로 가지 않는다. 이 값이 false여야 prepareAndImportScoreAsync가
            // 스코어를 realm에 쓰지 않는다.
            ShowResults = false,
            ShowLeaderboard = false,
        })
    {
        this.sectionStartTime = sectionStartTime;
    }

    /// <summary>
    /// 서버에 활동 상태를 알리지 않는다. 결과창(<c>ResultsScreen</c>)과 <c>PlayerLoader</c>의
    /// 활동 상태도 null이므로, 이 화면에 들어와도 <b>브로드캐스트 값이 바뀌지 않아 아무것도 전송되지 않는다.</b>
    /// 기본값은 <c>InSoloGame(비트맵, 룰셋)</c>이라 override하지 않으면 "플레이 중"이 전송된다.
    /// </summary>
    protected override UserActivity? InitialActivity => null;

    /// <summary>결과 화면 경로 자체를 쓰지 않는다 (<c>ShowResults = false</c>이므로 호출되지 않음).</summary>
    protected override ResultsScreen CreateResults(ScoreInfo score) =>
        throw new NotSupportedException("구간 연습은 결과 화면을 사용하지 않는다.");

    /// <summary>
    /// realm(client.realm)에 스코어/리플레이를 쓰지 않는다.
    /// 실패 오버레이의 "리플레이 저장"이 <c>forceImport: true</c>로 우회하므로 여기서 한 번 더 막는다.
    /// </summary>
    protected override Task ImportScore(Score score) => Task.CompletedTask;

    /// <summary>리플레이를 기록하지 않는다 — 저장되지도 제출되지도 않으므로 기록할 이유가 없다.</summary>
    protected override void PrepareReplay()
    {
    }

    protected override GameplayClockContainer CreateGameplayClockContainer(WorkingBeatmap beatmap, double gameplayStart) =>
        new SectionPracticeClockContainer(beatmap, gameplayStart);

    /// <summary>
    /// 트랙에 남아 있던 배속 조정을 걷어낸 뒤 우리 것을 얹는다.
    /// <para>
    /// <b>결과창으로 넘어간 직전 <c>Player</c>는 아직 살아 있다.</b> <c>progressToResults</c>가
    /// <c>Push</c>를 쓰므로 그 화면은 종료가 아니라 <b>중단</b>되고, 트랙 조정을 떼는
    /// <c>StopUsingBeatmapClock()</c>은 <c>Player.OnExiting</c>에만 있어서 실행되지 않는다.
    /// 그래서 DT 플레이 뒤 결과창 배경음악이 빠르게 재생되는 것이고(osu! 정상 동작),
    /// 그 상태에서 우리가 같은 트랙에 배속을 한 번 더 걸면 <b>1.5 × 1.5 = 2.25배</b>가 된다.
    /// </para>
    /// <para>
    /// <c>MusicController.ResetTrackAdjustments()</c>로는 못 뗀다 — 그쪽은 자기 <c>CurrentTrack</c>
    /// 래퍼의 조정만 건드리고, 게임플레이 클럭은 raw 트랙에 직접 물리기 때문이다(해당 메서드의
    /// 주석이 명시한다). 결과창에서 게임플레이를 시작하는 경로가 osu!에 없어서 이 상황 자체가
    /// 원래는 존재할 수 없다.
    /// </para>
    /// </summary>
    private partial class SectionPracticeClockContainer : MasterGameplayClockContainer
    {
        private readonly Track beatmapTrack;

        public SectionPracticeClockContainer(WorkingBeatmap working, double gameplayStartTime)
            : base(working, gameplayStartTime)
        {
            beatmapTrack = working.Track;
        }

        protected override void StartGameplayClock()
        {
            // 볼륨/밸런스는 건드리지 않는다 - 배속에만 관여하는 두 축만 초기화한다.
            beatmapTrack.RemoveAllAdjustments(AdjustableProperty.Tempo);
            beatmapTrack.RemoveAllAdjustments(AdjustableProperty.Frequency);

            base.StartGameplayClock();
        }
    }

    /// <summary>
    /// 구간 첫 노트보다 <see cref="lead_in_ms"/>만큼 앞에서 시작한다.
    /// <para>
    /// <b>클럭 조작을 여기서 하는 것이 중요하다.</b> <c>CreateGameplayClockContainer</c>에서 하면 컨테이너가
    /// 아직 트리에 붙기 전이라 seek이 반영되지 않고, 첫 프레임의 시각이 <b>결과창에서 재생 중이던 곡 위치</b>로
    /// 읽힌다 (실제로 "시작하자마자 종료"의 원인이었다). <c>StartGameplay</c>는 클럭이 완전히 준비된 뒤에 불린다.
    /// </para>
    /// </summary>
    protected override void StartGameplay()
    {
        double rate = ModUtils.CalculateRateWithMods(Mods.Value);
        if (!(rate > 0)) rate = 1;

        GameplayClockContainer.Reset(sectionStartTime - lead_in_ms * rate, startClock: true);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (!LoadedBeatmapSuccessfully)
            return;

        // 구간 밖 노트가 아예 없으므로 마지막 노트가 판정되는 순간 이 값이 참이 된다.
        ScoreProcessor.HasCompleted.BindValueChanged(completed =>
        {
            if (completed.NewValue)
                queueExit();
        });
    }

    /// <summary>구간이 끝났다. 마지막 판정을 볼 여유를 두고 이전 화면(결과창)으로 돌아간다.</summary>
    private void queueExit()
    {
        if (exitQueued)
            return;

        exitQueued = true;

        Scheduler.AddDelayed(() =>
        {
            if (this.IsCurrentScreen())
                this.Exit();
        }, exit_delay_ms);
    }
}

/// <summary>
/// 구간 연습용 로딩 화면. 구간만 남긴 비트맵과 출처 스코어의 룰셋·모드를 걸어두고, 나갈 때 되돌린다.
/// <para>
/// 결과창은 <c>DisallowExternalBeatmapRulesetChanges = true</c>라 Beatmap/Ruleset/Mods를 이미 lease하고
/// 있고, <c>OsuScreenStack</c>이 새 화면의 의존성을 <b>직전 화면의 의존성을 부모로</b> 만들기 때문에
/// (<c>OsuScreenStack.CreateLeasedDependencies</c>), 이 로더는 새 lease를 뜨지 않고 결과창 lease의 사본을
/// 받는다. 즉 <c>ReplayPlayerLoader</c>가 기대는 <b>"lease가 알아서 원복해준다"가 여기서는 성립하지 않는다</b> —
/// 직접 되돌려야 한다.
/// </para>
/// </summary>
public partial class SectionPracticePlayerLoader : PlayerLoader, ILocalOnlyPlayerLoader
{
    private readonly ScoreInfo sourceScore;
    private readonly double sectionStartTime;
    private readonly double sectionEndTime;

    private WorkingBeatmap? previousBeatmap;
    private RulesetInfo? previousRuleset;
    private IReadOnlyList<Mod>? previousMods;

    [Resolved]
    private AudioManager audio { get; set; } = null!;

    [Resolved]
    private MusicController musicController { get; set; } = null!;

    public SectionPracticePlayerLoader(ScoreInfo sourceScore, double sectionStartTime, double sectionEndTime)
        // 재시도할 때마다 이 팩토리가 다시 불리므로 구간이 그대로 유지된다.
        : base(() => new SectionPracticePlayer(sectionStartTime))
    {
        this.sourceScore = sourceScore;
        this.sectionStartTime = sectionStartTime;
        this.sectionEndTime = sectionEndTime;
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        previousBeatmap = Beatmap.Value;
        previousRuleset = Ruleset.Value;
        previousMods = Mods.Value;

        // 룰셋을 먼저 바꾼다 - 룰셋이 바뀌면 모드 목록이 초기화되므로 순서가 뒤집히면 모드가 날아간다.
        Ruleset.Value = sourceScore.Ruleset;
        Mods.Value = sourceScore.Mods;
        Beatmap.Value = new SectionPracticeBeatmap(previousBeatmap, sectionStartTime, sectionEndTime, audio);

        base.OnEntering(e);

        // 결과창에서 흐르던 배경음악을 끈다. 여기까지 오면 PlayerLoader가 AllowGlobalTrackControl을
        // false로 만든 뒤라 MusicController가 다시 틀지 않는다. 게임플레이 트랙은 잠시 뒤
        // 게임플레이 클럭이 구간 시작 지점에서 직접 돌린다.
        // requestedByUser: false여야 UserPauseRequested가 서지 않아 복귀 시 재생이 되살아난다.
        musicController.Stop();
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (previousBeatmap != null)
            Beatmap.Value = previousBeatmap;

        if (previousRuleset != null)
            Ruleset.Value = previousRuleset;

        if (previousMods != null)
            Mods.Value = previousMods;

        // 결과창으로 돌아가면 배경음악을 되살린다 (진입할 때 우리가 끈 것).
        musicController.EnsurePlayingSomething();

        return base.OnExiting(e);
    }
}
