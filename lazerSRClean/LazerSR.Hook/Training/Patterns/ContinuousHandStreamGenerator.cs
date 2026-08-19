using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 연속핸드스트림. 매 슬롯에 노트가 오고, <b>6반복(12슬롯) + 2휴식(4슬롯) = 16슬롯 주기</b>가
/// 마디에 정확히 두 번 들어간다. 마디당 32이벤트 · 60노트로 현재 가장 밀도가 높다.
/// </summary>
/// <remarks>
/// <para>
/// 6반복은 <c>[3동치][1동치]</c>를 여섯 번 되풀이한다. 3동치와 1동치가 겹치면 안 되므로
/// <b>1동치는 3동치가 뺀 컬럼 <c>x</c>로 강제</b>되고, 결국 <b>6반복은 <c>x</c> 하나로 완전히 결정된다.</b>
/// 그 결과 한 손은 12노트 트릴을, 다른 손은 6번의 점프잭을 치게 된다 — 이 패턴의 정의다.
/// </para>
/// <code>
/// 슬롯 0…11 : [x̄ 3동치][x 1동치] × 6
/// 슬롯 12   : A(2동치) ∌ x
/// 슬롯 13   : b(1동치) ∉ A
/// 슬롯 14   : C(2동치) ∌ b,  ∌ 다음 x
/// 슬롯 15   : 다음 x
/// </code>
/// <para>
/// <b>마지막 1동치를 고르는 것이 곧 다음 6반복을 고르는 것</b>이다(다음 3동치와 겹치면 안 되므로
/// 그 값이 다음 <c>x</c>가 된다). 덕분에 마디 경계도 이것 하나로 이어진다 — 다음 마디가 직전 마디의
/// 슬롯 31을 읽으면 첫 <c>x</c>가 나온다.
/// </para>
/// <para>
/// <b><c>x</c>는 봉지에서 뽑는다.</b> 컬럼 4개를 섞어 하나씩 소진하므로 <b>연속한 6반복 4개
/// (= 2마디 = 한 세트)에 네 컬럼이 정확히 한 번씩</b> 들어간다. 봉지 안에서는 값이 서로 달라
/// "연속으로 같은 6반복 금지"가 자동으로 지켜지고, 봉지가 바뀌는 경계에서만 한 번 확인하면 된다.
/// 손 교대는 강제하지 않는다 — 같은 손이 두 번 이어서 트릴할 수 있다.
/// </para>
/// <para>
/// 컬럼 균등도 거의 따라온다. 6반복 12슬롯에서 3동치 컬럼 3개가 각 6회, 단노트가 6회로
/// <b>네 컬럼이 정확히 6노트씩</b>이다. 마디의 48노트가 균등하고 휴식의 12노트만 흔들린다.
/// </para>
/// <para>
/// 데드락은 없다. <c>C</c>는 <c>{b, 다음 x}</c>를 뺀 2~3개 컬럼에서 고르므로 후보가 항상 최소 하나다.
/// 다른 패턴이 직전에 온 경우에는 봉지를 새로 열고 시작한다 — 첫 슬롯이 3동치라 직전 마디 꼬리와
/// 겹치는 것을 피할 방법이 사실상 없고, 패턴 전환은 세트 사이(휴식 구간)에서만 일어난다.
/// </para>
/// </remarks>
public class ContinuousHandStreamGenerator : IPatternGenerator
{
    private const int cycle_slots = 16;

    /// <summary>6반복이 차지하는 슬롯 수. 이벤트 12개 = 3동치 6개 + 1동치 6개.</summary>
    private const int repeat_slots = 12;

    private const int repeats = repeat_slots / 2;

    private const int cycles_per_measure = TrainingGrid.SLOTS_PER_MEASURE / cycle_slots;

    private const int all_columns_mask = (1 << TrainingGrid.COLUMNS) - 1;

    private readonly int[] bag = new int[TrainingGrid.COLUMNS];

    private int bagIndex = TrainingGrid.COLUMNS;
    private int lastDrawn = -1;
    private int currentX = -1;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        // 직전 마디를 우리가 만들었으면 슬롯 31이 다음 x를 들고 있다.
        bool continuous = previous != null
                          && currentX >= 0
                          && previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 1) == 1 << currentX;

        if (!continuous)
        {
            bagIndex = bag.Length;
            lastDrawn = -1;
            currentX = drawColumn(rng);
        }

        var measure = new TrainingMeasure();

        for (int cycle = 0; cycle < cycles_per_measure; cycle++)
        {
            int nextX = drawColumn(rng);

            writeCycle(rng, measure, cycle * cycle_slots, currentX, nextX);

            currentX = nextX;
        }

        return measure;
    }

    private void writeCycle(Random rng, TrainingMeasure measure, int baseSlot, int x, int nextX)
    {
        for (int i = 0; i < repeats; i++)
        {
            int chordSlot = baseSlot + i * 2;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if (column != x)
                    measure[chordSlot, column] = SlotState.Note;
            }

            measure[chordSlot + 1, x] = SlotState.Note;
        }

        int restSlot = baseSlot + repeat_slots;

        int first = pickPair(rng, all_columns_mask & ~(1 << x));
        int second = pickColumn(rng, all_columns_mask & ~first);
        int third = pickPair(rng, all_columns_mask & ~(1 << second) & ~(1 << nextX));

        writeMask(measure, restSlot, first);
        measure[restSlot + 1, second] = SlotState.Note;
        writeMask(measure, restSlot + 2, third);
        measure[restSlot + 3, nextX] = SlotState.Note;
    }

    /// <summary>봉지에서 <c>x</c>를 하나 꺼낸다. 비면 새로 섞되 직전 값과 이어지지 않게 한다.</summary>
    private int drawColumn(Random rng)
    {
        if (bagIndex >= bag.Length)
        {
            do
            {
                for (int i = 0; i < bag.Length; i++)
                    bag[i] = i;

                for (int i = bag.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
            } while (lastDrawn >= 0 && bag[0] == lastDrawn);

            bagIndex = 0;
        }

        lastDrawn = bag[bagIndex++];

        return lastDrawn;
    }

    /// <summary>마스크 안에서 서로 다른 두 컬럼을 균등하게 뽑아 마스크로 돌려준다.</summary>
    private static int pickPair(Random rng, int mask)
    {
        var pairs = new int[TrainingGrid.COLUMNS * (TrainingGrid.COLUMNS - 1) / 2];
        int count = 0;

        for (int a = 0; a < TrainingGrid.COLUMNS; a++)
        {
            if ((mask & (1 << a)) == 0)
                continue;

            for (int b = a + 1; b < TrainingGrid.COLUMNS; b++)
            {
                if ((mask & (1 << b)) != 0)
                    pairs[count++] = (1 << a) | (1 << b);
            }
        }

        return count == 0 ? mask : pairs[rng.Next(count)];
    }

    private static int pickColumn(Random rng, int mask)
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

    private static void writeMask(TrainingMeasure measure, int slot, int mask)
    {
        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((mask & (1 << column)) != 0)
                measure[slot, column] = SlotState.Note;
        }
    }
}
