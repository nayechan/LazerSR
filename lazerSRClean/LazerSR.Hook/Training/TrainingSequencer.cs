using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.UI;

namespace LazerSR.Hook.Training;

/// <summary>세트 하나가 차지하는 시간 구간과 노트 수. 집계기가 판정을 귀속시키는 데 쓴다.</summary>
public record TrainingSetPlan(int SetIndex, double StartMs, double EndMs, int NoteCount);

/// <summary>세트를 중간에 끊었을 때 <b>실제로 나간</b> 노트 수와 끝 시각.</summary>
public record TrainingSetTruncation(int NoteCount, double EndMs);

/// <summary>
/// 마디 큐를 들고 있다가 때가 되면 <see cref="Playfield"/>에 노트를 주입한다.
/// <para>
/// <b>세트 편성은 탐색 알고리즘이 한다.</b> 시퀀서는 "이 생성기로 이 bpm에 몇 마디, 뒤에 휴식 얼마"를
/// 받아 마디로 풀어 큐에 넣고 시각을 계산하는 기계다. 생성기는 슬롯만 다루므로
/// <b>슬롯 → ms 변환은 전적으로 여기서</b> 일어난다.
/// </para>
/// <para>
/// 판정창은 비트맵이 아니라 <c>ApplyDefaults</c>에 넘기는 난이도 객체가 정한다. 주입을 우리가 하므로
/// 측정 구간의 노트만 고정 OD로 돌릴 수 있다 — 사용자 설정 OD와 분리된다.
/// </para>
/// </summary>
public class TrainingSequencer
{
    /// <summary>
    /// 주입 선행 시간. 스크롤 시간보다 커야 노트가 화면 중간에 팝인하지 않는다.
    /// <b>세트 결과로 다음 세트를 정하려면 세트 사이 휴식이 이 값보다 커야 한다.</b>
    /// </summary>
    public const double LOOKAHEAD_MS = 700;

    /// <summary>편성이 늦어졌을 때 확보할 최소 여유.</summary>
    private const double schedule_margin_ms = 200;

    private readonly Playfield playfield;
    private readonly IBeatmap beatmap;
    private readonly IBeatmapDifficultyInfo injectionDifficulty;
    private readonly Random rng = new Random();
    private readonly Queue<ScheduledMeasure> queue = new Queue<ScheduledMeasure>();

    private TrainingMeasure? lastGenerated;
    private double nextStartMs;
    private int nextSetIndex;

    /// <summary>지금 큐에 남아 있는 마디들이 속한 세트. 중단은 이 세트에만 적용된다.</summary>
    private int queuedSetIndex = -1;

    private double queuedMeasureMs;
    private int emittedNoteCount;
    private double emittedEndMs;

    /// <param name="overallDifficulty">주입되는 노트에 적용할 OD. 측정 구간은 8.5로 고정한다.</param>
    public TrainingSequencer(Playfield playfield, IBeatmap beatmap, double firstStartMs, double overallDifficulty)
    {
        this.playfield = playfield;
        this.beatmap = beatmap;

        nextStartMs = firstStartMs;

        var source = beatmap.Difficulty;

        injectionDifficulty = new BeatmapDifficulty
        {
            CircleSize = source.CircleSize,
            OverallDifficulty = (float)overallDifficulty,
            DrainRate = source.DrainRate,
            ApproachRate = source.ApproachRate,
        };
    }

    /// <summary>
    /// 이 시각 이전에는 세트를 시작할 수 없다. 배경음이 곡의 다운비트에 맞춰 시작 시각을 고를 때
    /// 이 값을 하한으로 쓴다.
    /// </summary>
    public double EarliestStartMs(double currentTimeMs) =>
        Math.Max(nextStartMs, currentTimeMs + LOOKAHEAD_MS + schedule_margin_ms);

    /// <summary>
    /// 세트 하나를 편성한다. 마디는 이 자리에서 전부 생성되고, 시각도 확정된다.
    /// 편성이 늦어 <see cref="LOOKAHEAD_MS"/>를 확보할 수 없으면 시작 시각을 미뤄 팝인을 막는다.
    /// </summary>
    /// <param name="alignedStartMs">
    /// 배경음의 다운비트에 맞춘 시작 시각. 주면 그것을 쓰되 <see cref="EarliestStartMs"/> 아래로는 내려가지 않는다.
    /// </param>
    public TrainingSetPlan EnqueueSet(IPatternGenerator generator, double bpm, int measureCount, double restMs, double currentTimeMs, double? alignedStartMs = null)
    {
        double measureMs = TrainingGrid.MeasureMs(bpm);
        double slotMs = TrainingGrid.SlotMs(bpm);

        double earliest = EarliestStartMs(currentTimeMs);
        double startMs = alignedStartMs is double aligned ? Math.Max(aligned, earliest) : earliest;
        int noteCount = 0;

        queuedSetIndex = nextSetIndex;
        queuedMeasureMs = measureMs;
        emittedNoteCount = 0;
        emittedEndMs = startMs;

        for (int i = 0; i < measureCount; i++)
        {
            var measure = generator.Generate(rng, lastGenerated);
            lastGenerated = measure;

            noteCount += countNotes(measure);
            queue.Enqueue(new ScheduledMeasure(measure, startMs + i * measureMs, slotMs, countNotes(measure)));
        }

        double endMs = startMs + measureCount * measureMs;

        nextStartMs = endMs + restMs;

        return new TrainingSetPlan(nextSetIndex++, startMs, endMs, noteCount);
    }

    /// <summary>
    /// 마디 경계 연속성을 끊는다. 세부패턴이 바뀔 때 호출한다 — 앞 패턴의 마지막 마디를 새 생성기에
    /// 넘기면 컬럼 회피 규칙이 엉뚱한 기준으로 걸린다(각 생성기가 방어는 하지만 의미가 없는 제약이다).
    /// </summary>
    public void ResetContinuity() => lastGenerated = null;

    /// <summary>
    /// 아직 나가지 않은 마디를 버린다. <b>이미 주입된 노트는 그대로 내려온다</b> — 중단은
    /// "더 이상 새로 내보내지 않는다"는 뜻이지 화면의 노트를 지우는 것이 아니다.
    /// <para>
    /// 세트의 끝 시각도 실제로 나간 마지막 마디의 끝으로 당긴다. 그러지 않으면 다음 세트가
    /// 원래 예정됐던 끝 시각까지 기다리게 된다.
    /// </para>
    /// </summary>
    public TrainingSetTruncation StopEmitting()
    {
        queue.Clear();
        nextStartMs = emittedEndMs;

        return new TrainingSetTruncation(emittedNoteCount, emittedEndMs);
    }

    public void Update(double currentTimeMs)
    {
        while (queue.Count > 0 && queue.Peek().StartMs <= currentTimeMs + LOOKAHEAD_MS)
            inject(queue.Dequeue());
    }

    private void inject(ScheduledMeasure scheduled)
    {
        emittedNoteCount += scheduled.NoteCount;
        emittedEndMs = scheduled.StartMs + queuedMeasureMs;

        for (int slot = 0; slot < TrainingGrid.SLOTS_PER_MEASURE; slot++)
        {
            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if (scheduled.Measure[slot, column] != SlotState.Note)
                    continue;

                var note = new Note
                {
                    StartTime = scheduled.StartMs + slot * scheduled.SlotMs,
                    Column = column,
                };

                // 판정창/중첩 오브젝트는 여기서 만들어진다 — 호출하지 않으면 판정이 기본값으로 어긋난다.
                note.ApplyDefaults(beatmap.ControlPointInfo, injectionDifficulty);

                playfield.Add(note);
            }
        }
    }

    private static int countNotes(TrainingMeasure measure)
    {
        int count = 0;

        for (int slot = 0; slot < TrainingGrid.SLOTS_PER_MEASURE; slot++)
        {
            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if (measure[slot, column] == SlotState.Note)
                    count++;
            }
        }

        return count;
    }

    private record ScheduledMeasure(TrainingMeasure Measure, double StartMs, double SlotMs, int NoteCount);
}
