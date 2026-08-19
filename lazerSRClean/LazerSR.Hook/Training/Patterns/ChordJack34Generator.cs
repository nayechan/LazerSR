using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 34코드잭. 24코드잭에서 2동치 자리가 3동치로 바뀐 것이다 — 슬롯 배치와 간격은 완전히 동일하다.
/// <para>
/// 4동치는 슬롯 0,4,8,…,28, 3동치는 슬롯 2,6,10,…,30. 마디당 이벤트 16개 = 노트 56개.
/// </para>
/// <para>
/// 3동치는 "어느 컬럼을 뺄까"가 유일한 자유 변수다(4가지). 그래서 컬럼 균등 조건이
/// <b>제외 컬럼이 각각 정확히 2번씩</b>으로 곧장 떨어져, 24코드잭의 상보 쌍 가중 추첨이
/// 여기서는 필요 없다 — 멀티셋 {0,0,1,1,2,2,3,3}을 섞는 것만으로 균등하다.
/// 가능한 배열은 8!/(2!)⁴ = 2,520가지이며, 제외 컬럼이 왼손·오른손에 4번씩 갈리므로 손 부하도 자동 대칭이다.
/// </para>
/// </summary>
public class ChordJack34Generator : IPatternGenerator
{
    /// <summary>4동치끼리의 간격. 그 중간(2슬롯)에 3동치가 들어간다.</summary>
    private const int chord_interval_slots = 4;

    /// <summary>마디당 4동치 개수이자 3동치 개수. 32 / 4 = 8.</summary>
    private const int chords_per_measure = TrainingGrid.SLOTS_PER_MEASURE / chord_interval_slots;

    /// <summary>컬럼별 제외 횟수. 8개의 3동치가 각 컬럼을 6번씩 쓰려면 제외는 2번씩이어야 한다.</summary>
    private const int omissions_per_column = chords_per_measure / TrainingGrid.COLUMNS;

    /// <summary>마디 경계가 항상 결정론적(3동치 → 4동치, 간격 고정)이라 <paramref name="previous"/>를 보지 않는다.</summary>
    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        var measure = new TrainingMeasure();
        int[] omitted = buildOmittedColumns(rng);

        for (int i = 0; i < chords_per_measure; i++)
        {
            int quadSlot = i * chord_interval_slots;
            int tripleSlot = quadSlot + chord_interval_slots / 2;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                measure[quadSlot, column] = SlotState.Note;

                if (column != omitted[i])
                    measure[tripleSlot, column] = SlotState.Note;
            }
        }

        return measure;
    }

    /// <summary>균형 조건을 만족하는 제외 컬럼 8개를 뽑아 무작위 순서로 돌려준다.</summary>
    private static int[] buildOmittedColumns(Random rng)
    {
        var bag = new int[chords_per_measure];

        for (int i = 0; i < bag.Length; i++)
            bag[i] = i / omissions_per_column;

        for (int i = bag.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        return bag;
    }
}
