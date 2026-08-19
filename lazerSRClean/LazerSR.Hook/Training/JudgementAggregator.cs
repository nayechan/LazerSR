using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace LazerSR.Hook.Training;

/// <summary>
/// 판정을 세트 단위로 모은다. **정확도를 계산하는 유일한 곳**이며, 탐색 알고리즘과 위젯이 같은 값을 본다.
/// <para>
/// 공식은 분모 320, 분자 <c>[320,300,200,100,50,0]</c>의 산술평균이다. 이 척도가 lazer mania의
/// 네이티브 판정 등급과 정확히 일치하므로 자체 판정창 없이 <see cref="JudgementResult.Type"/>을 그대로 쓴다.
/// </para>
/// <para>
/// 판정이 어느 세트에 속하는지는 <b>노트의 시각으로 역산</b>한다 — 시퀀서가 세트마다 시간 구간을
/// 알려주므로 별도의 태그 자료구조가 필요 없다. 시드 비트맵의 진입용 노트처럼 어느 구간에도 없는
/// 판정은 무시된다.
/// </para>
/// </summary>
public class JudgementAggregator : IDisposable
{
    private const double judgement_scale = 320.0;

    private readonly ScoreProcessor scoreProcessor;
    private readonly LinkedList<PendingSet> pending = new LinkedList<PendingSet>();

    /// <summary>세트가 끝났을 때 (세트 번호, 정확도 0~1). 등록 순서대로만 발화한다.</summary>
    public event Action<int, double>? SetCompleted;

    public JudgementAggregator(ScoreProcessor scoreProcessor)
    {
        this.scoreProcessor = scoreProcessor;
        scoreProcessor.NewJudgement += onNewJudgement;
    }

    /// <summary>시퀀서가 세트를 편성한 직후 호출한다.</summary>
    public void RegisterSet(int setIndex, double startMs, double endMs, int noteCount)
    {
        pending.AddLast(new PendingSet(setIndex, startMs, endMs, noteCount));

        if (pending.Count == 1)
            publishCurrent();
    }

    /// <summary>
    /// 세트를 중간에 끊었을 때 <b>실제로 나간 만큼</b>으로 완료 조건을 정정한다.
    /// <para>
    /// 이걸 하지 않으면 그 세트는 오지 않을 판정을 영원히 기다리고, 세션이 그 자리에서 멎는다.
    /// 정정 시점에 이미 조건을 채웠을 수도 있으므로 여기서 곧바로 완료 검사를 돌린다.
    /// </para>
    /// </summary>
    public void TruncateSet(int setIndex, int noteCount, double endMs)
    {
        for (var node = pending.First; node != null; node = node.Next)
        {
            if (node.Value.SetIndex != setIndex)
                continue;

            node.Value.Truncate(noteCount, endMs);
            break;
        }

        drainCompleted();
    }

    private void onNewJudgement(JudgementResult result)
    {
        int value = numeratorFor(result.Type);

        if (value < 0)
            return;

        double time = result.HitObject.StartTime;

        for (var node = pending.First; node != null; node = node.Next)
        {
            var set = node.Value;

            // 구간은 반열림이다. 휴식이 없는 지속상승에서는 앞 세트의 EndMs와 뒤 세트의 StartMs가
            // 정확히 같아지므로, 닫힌 구간으로 두면 뒤 세트의 첫 노트가 앞 세트로 잘못 들어간다.
            if (time < set.StartMs || time >= set.EndMs)
                continue;

            set.Numerator += value;
            set.Judged++;
            break;
        }

        publishCurrent();
        drainCompleted();
    }

    /// <summary>
    /// 세트는 <b>등록 순서대로만</b> 완료 통지한다. 휴식이 없는 지속상승에서는 앞 세트의 미스 판정과
    /// 뒤 세트의 첫 판정이 잠깐 겹칠 수 있는데, 순서를 강제하면 그 영향이 사라진다.
    /// </summary>
    private void drainCompleted()
    {
        while (pending.First != null && pending.First.Value.IsComplete)
        {
            var set = pending.First.Value;
            pending.RemoveFirst();

            SetCompleted?.Invoke(set.SetIndex, set.Accuracy);
        }

        publishCurrent();
    }

    private void publishCurrent()
    {
        var head = pending.First?.Value;

        TrainingSessionState.CurrentAccuracy.Value = head?.Accuracy ?? 1.0;
        TrainingSessionState.SetIndex.Value = head?.SetIndex ?? 0;
    }

    /// <summary>점수에 관여하지 않는 결과(홀드 유지 틱 등)는 -1을 돌려 집계에서 뺀다.</summary>
    private static int numeratorFor(HitResult type)
    {
        switch (type)
        {
            case HitResult.Perfect:
                return 320;

            case HitResult.Great:
                return 300;

            case HitResult.Good:
                return 200;

            case HitResult.Ok:
                return 100;

            case HitResult.Meh:
                return 50;

            case HitResult.Miss:
                return 0;

            default:
                return -1;
        }
    }

    public void Dispose() => scoreProcessor.NewJudgement -= onNewJudgement;

    private class PendingSet
    {
        public PendingSet(int setIndex, double startMs, double endMs, int noteCount)
        {
            SetIndex = setIndex;
            StartMs = startMs;
            EndMs = endMs;
            NoteCount = noteCount;
        }

        public int SetIndex { get; }
        public double StartMs { get; }
        public double EndMs { get; private set; }
        public int NoteCount { get; private set; }

        /// <summary>이미 집계된 판정은 건드리지 않는다 — 그 노트들은 실제로 나갔고 실제로 판정됐다.</summary>
        public void Truncate(int noteCount, double endMs)
        {
            NoteCount = noteCount;
            EndMs = endMs;
        }

        public long Numerator;
        public int Judged;

        public bool IsComplete => Judged >= NoteCount;

        /// <summary>집계된 노트가 없으면 100%.</summary>
        public double Accuracy => Judged == 0 ? 1.0 : Numerator / (judgement_scale * Judged);
    }
}
