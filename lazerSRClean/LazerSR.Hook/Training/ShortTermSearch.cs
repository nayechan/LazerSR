using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;

namespace LazerSR.Hook.Training;

/// <summary>
/// 단기 실력찾기. 세부패턴마다 적합 bpm 하나를 찾아 <see cref="TrainingProfile"/>에 남긴다.
/// <list type="number">
/// <item><b>지속상승</b> — 휴식 없이 세트마다 +1스냅. 정확도가 임계 이상이면 올리고, 미달이면 유지한다.
/// <b>연속 2세트 미달</b>이면 이탈한다. 여기서 도달한 bpm은 피로가 섞여 있어 기록하지 않는다.</item>
/// <item><b>휴식상승</b> — 이탈 bpm에서 1스냅 내려 시작한다. 세트 사이에 휴식을 넣고,
/// 통과(임계 이상)면 +1스냅, 미달이면 −1스냅. <b>어떤 bpm이 누적 3회 미달</b>하면 그 bpm의
/// 1스냅 아래를 최종값으로 확정한다.</item>
/// <item><b>휴식 모드</b> — 결과를 보여주고 저장/폐기 응답을 기다린다. 응답 전에는 노트를 만들지 않는다.</item>
/// </list>
/// <para>
/// <b>정확도 임계는 하나뿐이고 두 단계가 같은 값을 쓴다.</b> 패턴별로 다르지 않으며 대기 화면에서
/// 사용자가 직접 입력한 값이 그대로 내려온다 (무한 세션은 자기 몫의 임계를 따로 갖는다).
/// </para>
/// <para>
/// <b>파이프라인 지연</b>: 노트는 판정보다 <see cref="TrainingSequencer.LOOKAHEAD_MS"/> 먼저 주입돼야 한다.
/// 휴식이 있는 구간은 휴식이 그 시간을 흡수하므로 세트 하나만 미리 잡아두면 되지만,
/// <b>휴식이 없는 지속상승은 두 세트를 미리 잡아야</b> 노트가 끊기지 않는다. 그래서 지속상승에서는
/// 세트 N의 결과가 세트 N+2에 반영된다 — 상승 구간이라 1세트 지연의 대가가 거의 없다.
/// </para>
/// </summary>
public class ShortTermSearch
{
    public enum Phase
    {
        Idle,
        ContinuousClimb,
        RestClimb,
        AwaitingDecision,
        Finished,
    }

    private const int search_measures_per_set = 2;
    private const int continuous_exit_misses = 2;
    private const int fails_to_finalise = 3;

    /// <summary>휴식이 없는 지속상승만 두 세트를 미리 잡는다.</summary>
    private const int continuous_sets_in_flight = 2;
    private const int rest_sets_in_flight = 1;

    /// <summary>단기 측정의 유일한 정확도 임계(%). 지속상승·휴식상승이 같은 값을 쓴다.</summary>
    private readonly double accuracyThreshold;

    private readonly TrainingSequencer sequencer;
    private readonly JudgementAggregator aggregator;
    private readonly TrainingMusicPlayer? music;
    private readonly Random rng = new Random();

    private readonly List<SubPattern> order = new List<SubPattern>();
    private readonly Dictionary<int, SetContext> setContexts = new Dictionary<int, SetContext>();
    private readonly Dictionary<double, int> failCounts = new Dictionary<double, int>();

    private int currentIndex = -1;
    private SubPattern? current;
    private IPatternGenerator? generator;

    private double currentBpm;
    private int consecutiveMisses;
    private int setsInFlight;
    private double finalisedBpm;

    private readonly Dictionary<SlotPosition, CancellationTokenSource> starRatingRequests = new Dictionary<SlotPosition, CancellationTokenSource>();
    private readonly ConcurrentQueue<StarRatingResult> starRatingResults = new ConcurrentQueue<StarRatingResult>();

    public Phase CurrentPhase { get; private set; } = Phase.Idle;

    /// <param name="accuracyThreshold">단기 측정 임계(%). 대기 화면에서 사용자가 정한 값.</param>
    public ShortTermSearch(TrainingSequencer sequencer, JudgementAggregator aggregator, double accuracyThreshold, TrainingMusicPlayer? music = null)
    {
        this.accuracyThreshold = accuracyThreshold;
        this.sequencer = sequencer;
        this.aggregator = aggregator;
        this.music = music?.Available == true ? music : null;

        aggregator.SetCompleted += onSetCompleted;
        TrainingSessionState.RestDecisionSubmitted += onRestDecision;
    }

    public void Start(IReadOnlyList<SubPattern> selected)
    {
        order.Clear();
        order.AddRange(selected);
        shuffle(order);

        TrainingSessionState.Reset();
        TrainingSessionState.Active.Value = true;
        TrainingSessionState.Phase.Value = TrainingPhase.ShortTerm;

        currentIndex = -1;
        advanceToNextSubPattern();
    }

    public void Update(double currentTimeMs)
    {
        applyPendingStarRatings();

        if (CurrentPhase != Phase.ContinuousClimb && CurrentPhase != Phase.RestClimb)
            return;

        int target = CurrentPhase == Phase.ContinuousClimb ? continuous_sets_in_flight : rest_sets_in_flight;

        // 편성이 실패해도 루프가 돌지 않도록 반환값으로 빠져나간다 (프레임이 통째로 멎는 사고 방지).
        while (setsInFlight < target && enqueueSet(currentTimeMs))
        {
        }
    }

    private bool enqueueSet(double currentTimeMs)
    {
        if (current == null || generator == null)
            return false;

        // 지속상승은 휴식이 없다. 휴식상승은 세부패턴별 휴식을 세트 뒤에 붙인다.
        double restMs = CurrentPhase == Phase.ContinuousClimb ? 0 : current.RestMs;

        double? alignedStartMs = null;

        if (music != null && music.Usable)
        {
            // 휴식이 있는 구간은 세트마다 곡을 바꾼다. 휴식이 없는 지속상승은 곡을 이어서 튼다 —
            // 세트가 맞붙어 있어 박자가 그대로 이어지고, 배속만 세트 시작 시각에 맞춰 바뀐다.
            if (restMs > 0 || !music.IsPlaying)
            {
                if (!music.TryBeginSong(currentBpm, sequencer.EarliestStartMs(currentTimeMs), currentTimeMs, out double downbeatMs))
                    return false; // 트랙이 아직 돌기 시작하지 않았다 — 다음 프레임에 다시 시도한다.

                alignedStartMs = downbeatMs;
            }
        }

        var plan = sequencer.EnqueueSet(generator, currentBpm, search_measures_per_set, restMs, currentTimeMs, alignedStartMs);

        setContexts[plan.SetIndex] = new SetContext(CurrentPhase, currentBpm);
        aggregator.RegisterSet(plan.SetIndex, plan.StartMs, plan.EndMs, plan.NoteCount);
        music?.RegisterSet(plan.StartMs, plan.EndMs, currentBpm);

        setsInFlight++;
        return true;
    }

    private void onSetCompleted(int setIndex, double accuracy)
    {
        if (!setContexts.Remove(setIndex, out var context))
            return;

        setsInFlight = Math.Max(0, setsInFlight - 1);

        // 단계가 바뀌기 전에 편성된 세트는 결과를 쓰지 않는다
        // (지속상승 이탈 시점에 이미 큐에 들어가 있던 세트가 여기 해당한다).
        if (context.Phase != CurrentPhase || current == null)
            return;

        double percent = accuracy * 100;

        if (CurrentPhase == Phase.ContinuousClimb)
            handleContinuous(percent, context.Bpm);
        else if (CurrentPhase == Phase.RestClimb)
            handleRestClimb(percent, context.Bpm);
    }

    private void handleContinuous(double percent, double setBpm)
    {
        if (percent >= accuracyThreshold)
        {
            consecutiveMisses = 0;
            currentBpm += current!.BpmSnap;
            publishCurrentSlot();
            return;
        }

        // 미달이면 bpm을 올리지 않고 버틴다. 연속 2회면 이탈.
        consecutiveMisses++;

        if (consecutiveMisses < continuous_exit_misses)
            return;

        // 이탈 기준은 "이탈을 유발한 세트의 bpm"이다. 지속상승은 두 세트를 미리 잡으므로
        // currentBpm이 이미 한 칸 앞서 있을 수 있어, 그 값을 쓰면 시작점이 밀린다.
        CurrentPhase = Phase.RestClimb;
        consecutiveMisses = 0;
        failCounts.Clear();
        currentBpm = Math.Max(current!.BpmSnap, setBpm - current.BpmSnap);
        publishCurrentSlot();
    }

    private void handleRestClimb(double percent, double setBpm)
    {
        if (percent >= accuracyThreshold)
        {
            currentBpm = setBpm + current!.BpmSnap;
            publishCurrentSlot();
            return;
        }

        failCounts.TryGetValue(setBpm, out int fails);
        fails++;
        failCounts[setBpm] = fails;

        if (fails >= fails_to_finalise)
        {
            finalise(setBpm - current!.BpmSnap);
            return;
        }

        currentBpm = Math.Max(current!.BpmSnap, setBpm - current.BpmSnap);
        publishCurrentSlot();
    }

    private void finalise(double bpm)
    {
        finalisedBpm = Math.Max(current!.BpmSnap, bpm);

        CurrentPhase = Phase.AwaitingDecision;
        setsInFlight = 0;
        setContexts.Clear();

        TrainingSessionState.RestingResult.Value = new TrainingSlot(current.DisplayName, finalisedBpm, 0);
        TrainingSessionState.Mode.Value = TrainingMode.Resting;

        requestStarRating(SlotPosition.Resting, current, finalisedBpm);
    }

    private void onRestDecision(bool save)
    {
        if (CurrentPhase != Phase.AwaitingDecision || current == null)
            return;

        if (save)
            TrainingProfile.SetShortTermBpm(current, finalisedBpm);

        advanceToNextSubPattern();
    }

    private void advanceToNextSubPattern()
    {
        currentIndex++;

        TrainingSessionState.Mode.Value = TrainingMode.Playing;
        TrainingSessionState.RestingResult.Value = null;

        if (currentIndex >= order.Count)
        {
            finishSession();
            return;
        }

        current = order[currentIndex];
        generator = current.CreateGenerator();

        sequencer.ResetContinuity();

        currentBpm = TrainingProfile.GetShortTermBpm(current);
        currentStarRating = 0;
        consecutiveMisses = 0;
        setsInFlight = 0;
        failCounts.Clear();
        setContexts.Clear();

        CurrentPhase = Phase.ContinuousClimb;

        publishNeighbourSlots();
        publishCurrentSlot();
    }

    private void finishSession()
    {
        // 무한세션이 아직 없으므로 노트를 만들지 않고 그대로 머문다.
        music?.StopAll();

        CurrentPhase = Phase.Finished;
        current = null;
        generator = null;

        TrainingSessionState.Current.Value = null;
        TrainingSessionState.Next.Value = null;
        TrainingSessionState.AfterNext.Value = null;
    }

    private void publishCurrentSlot()
    {
        if (current == null)
            return;

        // 직전 계산값을 그대로 실어 보낸다 — 세트마다 0.00★으로 깜빡이는 것을 막는다.
        TrainingSessionState.Current.Value = new TrainingSlot(current.DisplayName, currentBpm, currentStarRating);
        requestStarRating(SlotPosition.Current, current, currentBpm);
    }

    private void publishNeighbourSlots()
    {
        // 이전은 이미 측정이 끝나 프로필에 확정값이 들어가 있고, 다음/다다음은 아직 시작 전이라 초기값이다.
        publishSlot(SlotPosition.Previous, currentIndex - 1);
        publishSlot(SlotPosition.Next, currentIndex + 1);
        publishSlot(SlotPosition.AfterNext, currentIndex + 2);
    }

    private void publishSlot(SlotPosition position, int index)
    {
        var bindable = bindableFor(position);

        if (index < 0 || index >= order.Count)
        {
            cancelStarRating(position);
            bindable.Value = null;
            return;
        }

        var subPattern = order[index];
        double bpm = TrainingProfile.GetShortTermBpm(subPattern);

        bindable.Value = new TrainingSlot(subPattern.DisplayName, bpm, 0);
        requestStarRating(position, subPattern, bpm);
    }

    private static Bindable<TrainingSlot?> bindableFor(SlotPosition position)
    {
        switch (position)
        {
            case SlotPosition.Previous:
                return TrainingSessionState.Previous;

            case SlotPosition.Next:
                return TrainingSessionState.Next;

            case SlotPosition.AfterNext:
                return TrainingSessionState.AfterNext;

            case SlotPosition.Resting:
                return TrainingSessionState.RestingResult;

            default:
                return TrainingSessionState.Current;
        }
    }

    // ── sunnySR ──────────────────────────────────────────────────────────────
    // 합성 보면 생성 + 계산은 무겁지 않지만 게임플레이 스레드에서 돌릴 이유는 없다.
    // 결과는 필드에 담아두고 Update()에서 회수한다 — 스케줄러 없이 스레드 문제를 피한다.

    private double currentStarRating;

    /// <summary>
    /// 자리마다 독립된 합성 보면으로 계산한다. 같은 자리에 새 요청이 오면 앞선 요청은 취소한다.
    /// </summary>
    private void requestStarRating(SlotPosition position, SubPattern subPattern, double bpm)
    {
        cancelStarRating(position);

        var cts = new CancellationTokenSource();
        starRatingRequests[position] = cts;

        var token = cts.Token;
        var factory = subPattern.CreateGenerator;

        Task.Run(() =>
        {
            try
            {
                double sr = SyntheticPatternMap.CalculateSunnySr(factory(), bpm, token);

                if (!token.IsCancellationRequested)
                    starRatingResults.Enqueue(new StarRatingResult(position, bpm, sr));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] ShortTermSearch star rating failed: {e}");
            }
        }, token);
    }

    private void cancelStarRating(SlotPosition position)
    {
        if (starRatingRequests.Remove(position, out var existing))
            existing.Cancel();
    }

    /// <summary>
    /// 결과는 백그라운드에서 큐에 쌓이고 여기서 회수한다 — 스케줄러 없이 스레드 문제를 피한다.
    /// bpm이 그새 바뀐 결과는 버린다.
    /// </summary>
    private void applyPendingStarRatings()
    {
        while (starRatingResults.TryDequeue(out var result))
        {
            if (result.Position == SlotPosition.Current)
                currentStarRating = result.StarRating;

            var bindable = bindableFor(result.Position);
            var slot = bindable.Value;

            if (slot != null && Math.Abs(slot.Bpm - result.Bpm) < 0.001)
                bindable.Value = slot with { StarRating = result.StarRating };
        }
    }

    private void shuffle(List<SubPattern> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public void Dispose()
    {
        foreach (var cts in starRatingRequests.Values)
            cts.Cancel();

        starRatingRequests.Clear();

        aggregator.SetCompleted -= onSetCompleted;
        TrainingSessionState.RestDecisionSubmitted -= onRestDecision;
    }

    private enum SlotPosition
    {
        Previous,
        Current,
        Next,
        AfterNext,
        Resting,
    }

    private record SetContext(Phase Phase, double Bpm);

    private record StarRatingResult(SlotPosition Position, double Bpm, double StarRating);
}
