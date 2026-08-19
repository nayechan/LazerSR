using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 덴시핸드스트림. 싱글핸드스트림의 4슬롯 주기 <c>3,1,1,1</c>을 <c>3,1,2,1</c>로 바꾼 것이다.
/// 마디당 32이벤트 · 56노트.
/// </summary>
/// <remarks>
/// <para>
/// 싱글핸드스트림의 자유 슬롯(<c>4m+2</c>)은 양쪽 이웃 <c>x_m</c>·<c>x_{m+1}</c>을 둘 다 배제하고
/// 남은 <b>정확히 2개</b>에서 하나를 뽑는 자리였다. 그 둘을 다 놓으면 그게 곧 2동치이므로
/// <b>이 패턴에는 추첨이 없다</b> — 마디는 여집합 수열 <c>x₀…x₈</c>만으로 완전히 결정된다.
/// </para>
/// <code>
/// 슬롯 4m    : 3동치 = 전체 \ {x_m}
/// 슬롯 4m+1  : x_m
/// 슬롯 4m+2  : 2동치 = 전체 \ {x_m, x_{m+1}}
/// 슬롯 4m+3  : x_{m+1}
/// </code>
/// <para>
/// 인접 슬롯이 어디서도 겹치지 않으므로 간격 2 규칙이 전 구간에서 자동으로 성립하고,
/// 데드락·재시도·폴백이 필요 없다. 마디 경계 처리는 싱글핸드스트림과 동일하다 —
/// 마지막 노트(<c>x₈</c>)를 자유롭게 뽑아두고 다음 마디가 그것을 읽어 <c>x₀</c>로 삼는다.
/// </para>
/// </remarks>
public class DenseHandStreamGenerator : IPatternGenerator
{
    /// <summary>동시치기가 돌아오는 주기.</summary>
    private const int chord_interval_slots = 4;

    private const int chord_count = TrainingGrid.SLOTS_PER_MEASURE / chord_interval_slots;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        // x₀…x₈ — 인접한 값이 같으면 동시치기가 연속으로 같아지므로 항상 다르게 뽑는다.
        var omitted = new int[chord_count + 1];

        omitted[0] = readBoundaryColumn(rng, previous);

        for (int i = 1; i < omitted.Length; i++)
            omitted[i] = pickOtherThan(rng, omitted[i - 1]);

        var measure = new TrainingMeasure();

        for (int m = 0; m < chord_count; m++)
        {
            int chordSlot = m * chord_interval_slots;
            int before = omitted[m];
            int after = omitted[m + 1];

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if (column != before)
                    measure[chordSlot, column] = SlotState.Note;

                // before ≠ after이므로 남는 컬럼은 항상 정확히 2개다.
                if (column != before && column != after)
                    measure[chordSlot + 2, column] = SlotState.Note;
            }

            measure[chordSlot + 1, before] = SlotState.Note;
            measure[chordSlot + 3, after] = SlotState.Note;
        }

        return measure;
    }

    /// <summary>
    /// 직전 마디의 마지막 노트 컬럼을 <c>x₀</c>로 쓴다. 단노트가 아니면(다른 패턴이 직전에 온 경우)
    /// 규칙을 이을 수 없으므로 무작위로 시작한다.
    /// </summary>
    private static int readBoundaryColumn(Random rng, TrainingMeasure? previous)
    {
        TrainingMeasure.ReadTailMasks(previous, out int lastMask, out _);

        int found = -1;
        int count = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((lastMask & (1 << column)) == 0)
                continue;

            found = column;
            count++;
        }

        return count == 1 ? found : rng.Next(TrainingGrid.COLUMNS);
    }

    /// <summary>지정한 컬럼을 뺀 나머지 중 하나를 균등하게 뽑는다.</summary>
    private static int pickOtherThan(Random rng, int excluded)
    {
        int offset = rng.Next(TrainingGrid.COLUMNS - 1);

        return offset >= excluded ? offset + 1 : offset;
    }
}
