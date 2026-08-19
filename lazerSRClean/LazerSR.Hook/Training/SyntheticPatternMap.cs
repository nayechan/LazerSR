using System;
using System.Threading;
using LazerSR.SunnyCalculator;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;

namespace LazerSR.Hook.Training;

/// <summary>
/// sunnySR pill용 임시 합성 비트맵. 세부패턴 생성기를 반복해 돌려 메모리에만 존재하는 보면을 만든다.
/// <para>
/// <b>목표 노트 수를 채울 때까지 마디를 이어 붙인다.</b> 마디 수를 고정하면 패턴마다 마디당 노트 수가
/// 달라(32~48) sunnySR의 짧은 맵 보정 <c>W/(W+60)</c>이 다르게 걸려 난이도와 무관한 계통 오차가 생긴다.
/// 노트 수를 기준으로 맞추면 그 오차가 사라지고, 길이가 충분히 길어 운 나쁘게 어려운 마디가 몇 개
/// 나와도 값이 튀지 않는다.
/// </para>
/// <para>
/// sunnySR은 정렬 후 백분위·멱평균만 쓰므로 <b>노트의 시간 순서와 무관</b>하다. 그래서 생성기를 그냥
/// 이어 붙이기만 하면 되고, 매번 새로 뽑아도 값이 안정적이다.
/// </para>
/// </summary>
public static class SyntheticPatternMap
{
    /// <summary>
    /// 목표 노트 수. <c>W/(W+60)</c>이 0.94를 넘어가 패턴 간 밀도 차이로 인한 오차가 1~2%로 떨어진다.
    /// </summary>
    public const int TARGET_NOTE_COUNT = 1000;

    /// <summary>측정 단계와 같은 OD. 판정창은 SR 계산에 쓰이지 않지만 값을 맞춰둔다.</summary>
    private const float overall_difficulty = 8.5f;

    /// <summary>
    /// 합성 보면을 새로 만들어 sunnySR을 계산한다. **호출할 때마다 독립적인 무작위 보면을 만든다.**
    /// </summary>
    public static double CalculateSunnySr(IPatternGenerator generator, double bpm, CancellationToken token = default)
    {
        var beatmap = Build(generator, bpm, new Random(), token);

        return new SunnyManiaDifficultyCalculator().Calculate(beatmap, null, token);
    }

    public static ManiaBeatmap Build(IPatternGenerator generator, double bpm, Random rng, CancellationToken token = default)
    {
        double slotMs = TrainingGrid.SlotMs(bpm);
        double measureMs = TrainingGrid.MeasureMs(bpm);

        var beatmap = new ManiaBeatmap(new StageDefinition(TrainingGrid.COLUMNS))
        {
            BeatmapInfo = new BeatmapInfo
            {
                Difficulty = new BeatmapDifficulty
                {
                    CircleSize = TrainingGrid.COLUMNS,
                    OverallDifficulty = overall_difficulty,
                    DrainRate = 5,
                    ApproachRate = 5,
                },
            },
        };

        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 60000 / bpm });

        TrainingMeasure? previous = null;
        int measureIndex = 0;

        while (beatmap.HitObjects.Count < TARGET_NOTE_COUNT)
        {
            token.ThrowIfCancellationRequested();

            var measure = generator.Generate(rng, previous);
            previous = measure;

            double measureStart = measureIndex * measureMs;

            for (int slot = 0; slot < TrainingGrid.SLOTS_PER_MEASURE; slot++)
            {
                for (int column = 0; column < TrainingGrid.COLUMNS; column++)
                {
                    if (measure[slot, column] != SlotState.Note)
                        continue;

                    var note = new Note
                    {
                        StartTime = measureStart + slot * slotMs,
                        Column = column,
                    };

                    note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                    beatmap.HitObjects.Add(note);
                }
            }

            measureIndex++;
        }

        return beatmap;
    }
}
