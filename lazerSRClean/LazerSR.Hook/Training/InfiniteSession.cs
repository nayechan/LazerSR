using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LazerSR.Hook.Training;

/// <summary>
/// 무한 세션. 선택된 세부패턴을 <b>고정 bpm</b>으로 계속 돌린다 — 측정이 아니므로 bpm이 변하지 않고,
/// 끝나는 조건도 사용자가 ESC로 나가는 것뿐이다.
/// <list type="number">
/// <item><b>세트</b> — 패턴 하나를 40초를 넘지 않는 최대 마디 수만큼. bpm은 대기 화면에서 정한 값 그대로.</item>
/// <item><b>조기 종료</b> — 세트 누적 정확도가 목표 미만인 상태가 5초 이어지면 <b>더 이상 새 노트를
/// 내보내지 않는다</b>. 이미 나간 노트는 그대로 내려오고, 그것들이 전부 판정되면 세트가 끝난다.</item>
/// <item><b>휴식</b> — 세트의 마지막 노트가 판정된 시각부터 8초. 그 뒤 다음 패턴으로 넘어간다.</item>
/// </list>
/// <para>
/// 패턴은 <b>비복원 추첨</b>이다. 선택된 패턴을 모두 소진하면 봉지를 다시 채운다. 위젯이 다음·다다음을
/// 보여줘야 하므로 미리 세 개를 뽑아 큐에 들고 있는다.
/// </para>
/// <para>
/// bpm이 고정이라 <b>sunnySR도 패턴마다 한 번만 계산</b>하면 된다 — 단기 탐색처럼 자리별로 매번
/// 다시 계산할 이유가 없다.
/// </para>
/// </summary>
public class InfiniteSession
{
    /// <summary>세트 하나의 목표 길이. 마디 단위로 끊으므로 실제 길이는 이 값 이하다.</summary>
    private const double set_target_ms = 40_000;

    /// <summary>세트 사이 휴식. 마지막 노트가 판정된 시각부터 잰다.</summary>
    private const double rest_ms = 8_000;

    /// <summary>목표 정확도 미달이 이만큼 이어지면 세트를 끊는다. 세트 초반의 표본 부족도 이 시간이 흡수한다.</summary>
    private const double fail_hold_ms = 5_000;

    /// <summary>위젯이 현재·다음·다다음을 보여주므로 항상 이만큼 미리 뽑아둔다.</summary>
    private const int lookahead_picks = 3;

    private readonly TrainingSequencer sequencer;
    private readonly JudgementAggregator aggregator;
    private readonly TrainingMusicPlayer? music;
    private readonly double accuracyThreshold;
    private readonly Random rng = new Random();

    private readonly List<SubPattern> selected = new List<SubPattern>();
    private readonly List<SubPattern> bag = new List<SubPattern>();
    private readonly Queue<SubPattern> upcoming = new Queue<SubPattern>();

    private readonly Dictionary<string, double> starRatings = new Dictionary<string, double>();
    private readonly ConcurrentQueue<StarRatingResult> starRatingResults = new ConcurrentQueue<StarRatingResult>();
    private readonly List<CancellationTokenSource> starRatingRequests = new List<CancellationTokenSource>();

    private SubPattern? current;
    private SubPattern? previous;
    private IPatternGenerator? generator;

    private int activeSetIndex = -1;
    private bool setInFlight;
    private bool restPending;
    private double belowSinceMs = double.NegativeInfinity;
    private double restUntilMs = double.NegativeInfinity;

    public InfiniteSession(TrainingSequencer sequencer, JudgementAggregator aggregator, double accuracyThreshold, TrainingMusicPlayer? music = null)
    {
        this.sequencer = sequencer;
        this.aggregator = aggregator;
        this.accuracyThreshold = accuracyThreshold;
        this.music = music?.Available == true ? music : null;

        aggregator.SetCompleted += onSetCompleted;
    }

    public void Start(IReadOnlyList<SubPattern> patterns)
    {
        selected.Clear();
        selected.AddRange(patterns);

        TrainingSessionState.Reset();
        TrainingSessionState.Active.Value = true;
        TrainingSessionState.Phase.Value = TrainingPhase.Infinite;
        TrainingSessionState.Mode.Value = TrainingMode.Playing;

        bag.Clear();
        upcoming.Clear();

        foreach (var pattern in selected)
            requestStarRating(pattern);

        fillUpcoming();
        publishSlots();
    }

    public void Update(double currentTimeMs)
    {
        applyPendingStarRatings();

        if (selected.Count == 0)
            return;

        if (setInFlight)
        {
            checkEarlyStop(currentTimeMs);
            return;
        }

        // 세트 완료는 판정 콜백에서 오므로 그 자리에서는 시각을 알 수 없다.
        // 휴식의 기산점(= 마지막 노트가 판정된 시각)은 바로 다음 프레임의 시각으로 잡는다.
        if (restPending)
        {
            restUntilMs = currentTimeMs + rest_ms;
            restPending = false;
        }

        if (currentTimeMs < restUntilMs)
            return;

        beginSet(currentTimeMs);
    }

    // ── 세트 ─────────────────────────────────────────────────────────────────

    private void beginSet(double currentTimeMs)
    {
        if (current == null)
        {
            current = takeNext();
            generator = current.CreateGenerator();
            sequencer.ResetContinuity();
            publishSlots();
        }

        double bpm = TrainingProfile.GetShortTermBpm(current);
        int measureCount = measureCountFor(bpm);

        double? alignedStartMs = null;

        if (music != null && music.Usable)
        {
            // 휴식이 8초라 세트마다 곡을 새로 튼다. 정렬이 아직 안 잡혔으면 다음 프레임에 다시 시도한다.
            if (!music.TryBeginSong(bpm, sequencer.EarliestStartMs(currentTimeMs), currentTimeMs, out double downbeatMs))
                return;

            alignedStartMs = downbeatMs;
        }

        var plan = sequencer.EnqueueSet(generator!, bpm, measureCount, 0, currentTimeMs, alignedStartMs);

        aggregator.RegisterSet(plan.SetIndex, plan.StartMs, plan.EndMs, plan.NoteCount);
        music?.RegisterSet(plan.StartMs, plan.EndMs, bpm);

        activeSetIndex = plan.SetIndex;
        setInFlight = true;
        belowSinceMs = double.NegativeInfinity;

        TrainingSessionState.Mode.Value = TrainingMode.Playing;
    }

    /// <summary>40초를 넘지 않는 최대 마디 수. 최소 한 마디는 보장한다.</summary>
    private static int measureCountFor(double bpm) =>
        Math.Max(1, (int)(set_target_ms / TrainingGrid.MeasureMs(bpm)));

    /// <summary>
    /// 목표 미달이 <see cref="fail_hold_ms"/> 동안 이어지면 새 노트 공급만 끊는다.
    /// 세트의 완료 조건도 실제로 나간 만큼으로 정정해야 한다 — 안 그러면 오지 않을 판정을 기다린다.
    /// </summary>
    private void checkEarlyStop(double currentTimeMs)
    {
        double percent = TrainingSessionState.CurrentAccuracy.Value * 100;

        if (percent >= accuracyThreshold)
        {
            belowSinceMs = double.NegativeInfinity;
            return;
        }

        if (!double.IsFinite(belowSinceMs))
        {
            belowSinceMs = currentTimeMs;
            return;
        }

        if (currentTimeMs - belowSinceMs < fail_hold_ms)
            return;

        var truncation = sequencer.StopEmitting();

        belowSinceMs = double.NegativeInfinity;
        aggregator.TruncateSet(activeSetIndex, truncation.NoteCount, truncation.EndMs);
    }

    /// <summary>
    /// 세트의 마지막 노트가 판정된 순간이다 — 휴식은 여기서부터 잰다.
    /// 시각은 <see cref="TrainingSequencer"/>가 쓰는 게임플레이 시계와 같은 축이어야 하므로
    /// 다음 <see cref="Update"/>에서 잰 시각을 기준으로 삼는다.
    /// </summary>
    private void onSetCompleted(int setIndex, double accuracy)
    {
        if (setIndex != activeSetIndex)
            return;

        setInFlight = false;
        restPending = true;

        previous = current;
        current = null;
        generator = null;

        TrainingSessionState.Mode.Value = TrainingMode.Resting;
        TrainingSessionState.RestingResult.Value = slotFor(upcoming.Count > 0 ? upcoming.Peek() : null);

        publishSlots();
    }

    // ── 패턴 추첨 ────────────────────────────────────────────────────────────

    /// <summary>비복원 추첨. 봉지가 비면 선택된 패턴 전부로 다시 채운다.</summary>
    private SubPattern draw()
    {
        if (bag.Count == 0)
        {
            bag.AddRange(selected);
            shuffle(bag);
        }

        var pattern = bag[^1];
        bag.RemoveAt(bag.Count - 1);

        return pattern;
    }

    private void fillUpcoming()
    {
        while (upcoming.Count < lookahead_picks)
            upcoming.Enqueue(draw());
    }

    private SubPattern takeNext()
    {
        fillUpcoming();

        var pattern = upcoming.Dequeue();
        fillUpcoming();

        return pattern;
    }

    // ── 위젯 표시 ────────────────────────────────────────────────────────────

    private void publishSlots()
    {
        var queued = upcoming.ToArray();

        TrainingSessionState.Previous.Value = slotFor(previous);
        TrainingSessionState.Current.Value = slotFor(current ?? (queued.Length > 0 ? queued[0] : null));
        TrainingSessionState.Next.Value = slotFor(nth(queued, current == null ? 1 : 0));
        TrainingSessionState.AfterNext.Value = slotFor(nth(queued, current == null ? 2 : 1));
    }

    private static SubPattern? nth(SubPattern[] queued, int index) =>
        index >= 0 && index < queued.Length ? queued[index] : null;

    private TrainingSlot? slotFor(SubPattern? pattern)
    {
        if (pattern == null)
            return null;

        starRatings.TryGetValue(pattern.Id, out double starRating);

        return new TrainingSlot(pattern.DisplayName, TrainingProfile.GetShortTermBpm(pattern), starRating);
    }

    // ── sunnySR ──────────────────────────────────────────────────────────────
    // bpm이 세션 내내 고정이라 패턴마다 한 번만 계산하고 재사용한다.

    private void requestStarRating(SubPattern pattern)
    {
        if (starRatings.ContainsKey(pattern.Id))
            return;

        var cts = new CancellationTokenSource();
        starRatingRequests.Add(cts);

        var token = cts.Token;
        var factory = pattern.CreateGenerator;
        string id = pattern.Id;
        double bpm = TrainingProfile.GetShortTermBpm(pattern);

        Task.Run(() =>
        {
            try
            {
                double sr = SyntheticPatternMap.CalculateSunnySr(factory(), bpm, token);

                if (!token.IsCancellationRequested)
                    starRatingResults.Enqueue(new StarRatingResult(id, sr));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                HookLog.Write($"[LazerSR] InfiniteSession star rating failed: {e}");
            }
        }, token);
    }

    private void applyPendingStarRatings()
    {
        bool changed = false;

        while (starRatingResults.TryDequeue(out var result))
        {
            starRatings[result.Id] = result.StarRating;
            changed = true;
        }

        if (changed)
            publishSlots();
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
        foreach (var cts in starRatingRequests)
            cts.Cancel();

        starRatingRequests.Clear();

        aggregator.SetCompleted -= onSetCompleted;
    }

    private record StarRatingResult(string Id, double StarRating);
}
