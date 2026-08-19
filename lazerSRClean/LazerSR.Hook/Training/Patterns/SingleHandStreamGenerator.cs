using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 싱글핸드스트림. 매 슬롯에 노트가 오고, 4슬롯마다 3동시치기가 끼어든다.
/// <list type="number">
/// <item>모든 슬롯에 노트가 있다 (마디당 32이벤트 · 48노트).</item>
/// <item>슬롯 0,4,…,28에 <b>3동시치기</b>. 빠지는 컬럼 하나를 무작위로 고른다.</item>
/// <item>홀수 슬롯(동시치기 양옆)은 <b>그 동시치기의 여집합</b> 컬럼. 결과적으로
/// <c>[x][동시치기(x 제외)][x]</c> 샌드위치가 동시치기마다 강제로 생긴다.</item>
/// <item>남은 슬롯(2,6,…,30)은 <b>양쪽 이웃 둘 다 배제</b>하고 무작위.
/// 연속 동시치기가 서로 다르므로 후보는 항상 정확히 2개다.</item>
/// <item>연속한 두 동시치기는 서로 달라야 한다 — 같으면 3동치 코드잭과 단노트 3연속이 겹쳐 튄다.</item>
/// </list>
/// <para>
/// 마디는 <b>여집합 컬럼 수열 <c>x₀…x₈</c> 9개와 자유 슬롯 8개로 완전히 결정된다.</b>
/// 슬롯 <c>4m</c>이 <c>x_m</c>의 여집합, <c>4m+1</c>이 <c>x_m</c>, <c>4m+3</c>이 <c>x_{m+1}</c>,
/// <c>4m+2</c>가 자유다.
/// </para>
/// <para>
/// <b>마디 경계</b>: 마지막 노트 슬롯은 규칙상 "다음 동시치기의 여집합"이라 다음 마디를 알아야 정해진다.
/// 그래서 인과를 뒤집는다 — 그 자리(<c>x₈</c>)를 자유롭게 뽑아두고, <b>다음 마디가 직전 마디의 마지막
/// 노트를 읽어 그것을 <c>x₀</c>로 삼는다.</b> 그러면 다음 마디의 첫 동시치기가 자동으로 그 컬럼의
/// 여집합이 되어 샌드위치가 경계를 넘어 정확히 이어진다. 추가 상태는 필요 없다.
/// </para>
/// </summary>
public class SingleHandStreamGenerator : IPatternGenerator
{
    /// <summary>동시치기가 돌아오는 주기.</summary>
    private const int chord_interval_slots = 4;

    private const int chord_count = TrainingGrid.SLOTS_PER_MEASURE / chord_interval_slots;

    private const int all_columns_mask = (1 << TrainingGrid.COLUMNS) - 1;

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
            }

            measure[chordSlot + 1, before] = SlotState.Note;
            measure[chordSlot + 3, after] = SlotState.Note;

            // 양쪽 이웃을 배제 — before ≠ after이므로 후보는 항상 2개다.
            int freeMask = all_columns_mask & ~(1 << before) & ~(1 << after);
            measure[chordSlot + 2, pickFromMask(rng, freeMask)] = SlotState.Note;
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

    private static int pickFromMask(Random rng, int mask)
    {
        int count = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((mask & (1 << column)) != 0)
                count++;
        }

        int target = rng.Next(count);

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((mask & (1 << column)) == 0)
                continue;

            if (target-- == 0)
                return column;
        }

        return 0;
    }
}
