using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 점프스트림. 매 슬롯에 노트가 있고, 짝수 슬롯은 동치(2컬럼) · 홀수 슬롯은 단노트다.
/// 마디당 32이벤트 = 48노트.
/// </summary>
/// <remarks>
/// <para>
/// 같은 컬럼은 최소 2슬롯 떨어져야 하므로 제약은 <b>인접 슬롯끼리 겹치지 않을 것</b> 하나로 압축된다.
/// 동치를 <c>P_m</c>(슬롯 2m), 단노트를 <c>s_m</c>(슬롯 2m+1)이라 하면 <c>s_m ∉ P_m ∪ P_{m+1}</c>이고,
/// <c>P_m</c>과 <c>P_{m+1}</c>은 2슬롯 떨어져 있어 서로에 대한 제약이 없다.
/// <b>연속 동치가 상보(서로소)면 합집합이 4컬럼 전부가 되어 단노트를 놓을 자리가 사라지므로</b>
/// 상보만은 언제나 배제한다(데드락이 생기는 유일한 조합).
/// </para>
/// <para>
/// <b>극단 억제 2가지</b>(실기 피드백으로 추가):
/// </para>
/// <list type="number">
/// <item><b>한손 트릴 5노트 금지</b> — 한 손이 두 컬럼을 교대로 치는 구간을 4노트로 제한한다.</item>
/// <item><b>동일 동치 4연속 금지</b> — 같은 동치의 반복은 최대 3회까지.</item>
/// </list>
/// <para>
/// 트릴은 "동치 둘이 한 컬럼을 공유하면 그 손이 <c>c, 파트너, c</c>가 된다"는 국소 규칙으로 볼 수도
/// 있지만, <b>그 3노트는 앞뒤 단노트가 같은 컬럼이면 양옆으로 한 노트씩 더 늘어난다.</b> 그래서 국소
/// 판정으로는 5노트를 못 막고, <b>손별 교대 런 길이를 상태로 들고 다니며 슬롯을 놓을 때마다 갱신하는</b>
/// 방식을 쓴다. 후보는 (다음 동치, 단노트) 조합 단위로 검사한다 — 제약이 둘을 묶기 때문이다.
/// </para>
/// <para>
/// <b>데드락은 없다.</b> 한손 동치(<c>{1,2}</c>·<c>{3,4}</c>)를 놓으면 한 손은 2노트, 다른 손은 0노트가
/// 되어 <b>양손 런이 동시에 끊긴다</b> — 이것이 언제나 열려 있는 탈출구다. 현재 동치가 양손이면 한손 동치
/// 둘 다 상보가 아니라 항상 고를 수 있고, 현재 동치가 한손이면 동일 반복이 막혀도 양손 동치 4가지가 남는다.
/// 그래도 예외 상황에 대비해 후보가 0이 되면 트릴 제약만 한 단계 푼다(인접 규칙은 절대 풀지 않는다).
/// </para>
/// <para>
/// <b>상태를 갖는 생성기</b>(드르륵에 이은 두 번째)다. 마디 마지막 단노트는 다음 동치를 모른 채 뽑고,
/// 다음 마디가 상태를 읽어 <c>P_0</c>을 고르면서 경계 스텝의 제약을 뒤늦게 적용한다. 직전 마디가
/// 이 패턴이 만든 모양과 어긋나면(다른 패턴이 직전에 온 경우) 상태를 버리고 무작위로 시작한다.
/// </para>
/// </remarks>
public class JumpStreamGenerator : IPatternGenerator
{
    /// <summary>마디당 동치 개수이자 단노트 개수. 32 / 2 = 16.</summary>
    private const int jumps_per_measure = TrainingGrid.SLOTS_PER_MEASURE / 2;

    private const int full_mask = (1 << TrainingGrid.COLUMNS) - 1;

    /// <summary>한 손이 교대로 칠 수 있는 최대 노트 수. 5노트 금지이므로 4.</summary>
    private const int max_trill_notes = 4;

    /// <summary>같은 동치를 최대 몇 번 연속으로 쓸 수 있는지.</summary>
    private const int max_identical_run = 3;

    private const int hands = 2;

    /// <summary>가능한 동치 6가지를 컬럼 비트마스크로.</summary>
    private static readonly int[] pair_masks = buildPairMasks();

    private readonly int[] runLength = new int[hands];
    private readonly int[] runColumn = new int[hands];

    private int lastPair;
    private int lastSingle = -1;
    private int identicalRun = 1;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        var measure = new TrainingMeasure();
        int pair = beginMeasure(rng, previous);

        for (int m = 0; m < jumps_per_measure; m++)
        {
            int slot = m * 2;
            writeMask(measure, slot, pair);

            int single;

            if (m < jumps_per_measure - 1)
            {
                int next = pickStep(rng, pair, out single);

                applySlot(1 << single);
                applySlot(next);
                identicalRun = next == pair ? identicalRun + 1 : 1;
                pair = next;
            }
            else
            {
                // 다음 동치를 아직 모르므로 직전 동치만 피한다. 경계 스텝의 나머지 판정은
                // 다음 마디가 P_0을 고를 때 이 상태를 읽어 뒤늦게 적용한다.
                single = pickBoundarySingle(rng, pair);

                applySlot(1 << single);
                lastPair = pair;
                lastSingle = single;
            }

            measure[slot + 1, single] = SlotState.Note;
        }

        return measure;
    }

    /// <summary>
    /// 마디 첫 동치를 고른다. 직전 마디가 이 패턴이 만든 모양과 어긋나면 상태를 버리고 무작위로 시작한다.
    /// </summary>
    private int beginMeasure(Random rng, TrainingMeasure? previous)
    {
        bool continuous = previous != null
                          && lastPair != 0
                          && lastSingle >= 0
                          && previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 2) == lastPair
                          && previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 1) == 1 << lastSingle;

        int first;

        if (continuous)
        {
            first = pickBoundaryPair(rng);
            identicalRun = first == lastPair ? identicalRun + 1 : 1;
        }
        else
        {
            reset();
            first = pair_masks[rng.Next(pair_masks.Length)];
        }

        applySlot(first);

        return first;
    }

    private void reset()
    {
        for (int hand = 0; hand < hands; hand++)
        {
            runLength[hand] = 0;
            runColumn[hand] = -1;
        }

        lastPair = 0;
        lastSingle = -1;
        identicalRun = 1;
    }

    /// <summary>다음 동치와 그 앞 단노트를 함께 뽑는다. 제약이 둘을 묶으므로 조합 단위로 균등하게 고른다.</summary>
    private int pickStep(Random rng, int pair, out int single)
    {
        var nexts = new int[pair_masks.Length * TrainingGrid.COLUMNS];
        var singles = new int[nexts.Length];
        int complement = full_mask & ~pair;

        for (int relax = 0; relax < 2; relax++)
        {
            int count = 0;

            foreach (int next in pair_masks)
            {
                if (next == complement)
                    continue;

                if (next == pair && identicalRun >= max_identical_run)
                    continue;

                int freeMask = full_mask & ~(pair | next);

                for (int column = 0; column < TrainingGrid.COLUMNS; column++)
                {
                    if ((freeMask & (1 << column)) == 0)
                        continue;

                    if (relax == 0 && !fits(1 << column, next))
                        continue;

                    nexts[count] = next;
                    singles[count] = column;
                    count++;
                }
            }

            if (count == 0)
                continue;

            int index = rng.Next(count);
            single = singles[index];

            return nexts[index];
        }

        // 도달 불가능한 경로. 인접 규칙만은 지킨 채 아무거나 고른다.
        int fallback;

        do
        {
            fallback = pair_masks[rng.Next(pair_masks.Length)];
        } while (fallback == complement);

        single = pickFromMask(rng, full_mask & ~(pair | fallback));

        return fallback;
    }

    /// <summary>마디 첫 동치. 단노트는 직전 마디가 이미 정했으므로 동치만 고른다.</summary>
    private int pickBoundaryPair(Random rng)
    {
        var candidates = new int[pair_masks.Length];

        for (int relax = 0; relax < 3; relax++)
        {
            int count = 0;

            foreach (int candidate in pair_masks)
            {
                // 상보는 반드시 단노트를 포함하므로 이 조건에 이미 걸린다. 절대 풀지 않는다.
                if ((candidate & (1 << lastSingle)) != 0)
                    continue;

                if (relax < 2 && candidate == lastPair && identicalRun >= max_identical_run)
                    continue;

                if (relax < 1 && !fits(candidate))
                    continue;

                candidates[count++] = candidate;
            }

            if (count > 0)
                return candidates[rng.Next(count)];
        }

        return lastPair;
    }

    /// <summary>마디 마지막 단노트. 다음 동치를 모르므로 직전 동치만 피한다.</summary>
    private int pickBoundarySingle(Random rng, int pair)
    {
        var candidates = new int[TrainingGrid.COLUMNS];
        int freeMask = full_mask & ~pair;

        for (int relax = 0; relax < 2; relax++)
        {
            int count = 0;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if ((freeMask & (1 << column)) == 0)
                    continue;

                if (relax == 0 && !fits(1 << column))
                    continue;

                candidates[count++] = column;
            }

            if (count > 0)
                return candidates[rng.Next(count)];
        }

        return pickFromMask(rng, freeMask);
    }

    /// <summary>슬롯 하나(또는 둘)를 놓아도 트릴 상한을 넘지 않는지. 상태는 건드리지 않는다.</summary>
    private bool fits(int maskA) => fits(maskA, 0);

    private bool fits(int maskA, int maskB)
    {
        int length0 = runLength[0], length1 = runLength[1];
        int column0 = runColumn[0], column1 = runColumn[1];

        bool ok = applyChecked(maskA) && (maskB == 0 || applyChecked(maskB));

        runLength[0] = length0;
        runLength[1] = length1;
        runColumn[0] = column0;
        runColumn[1] = column1;

        return ok;
    }

    private bool applyChecked(int mask)
    {
        applySlot(mask);

        return runLength[0] <= max_trill_notes && runLength[1] <= max_trill_notes;
    }

    /// <summary>
    /// 슬롯 하나를 놓고 손별 교대 런을 갱신한다. 그 손에 노트가 하나가 아니면(동치이거나 쉬면) 런이 끊긴다.
    /// </summary>
    private void applySlot(int mask)
    {
        for (int hand = 0; hand < hands; hand++)
        {
            int handMask = mask >> (hand * 2) & 0b11;

            if (handMask != 0b01 && handMask != 0b10)
            {
                runLength[hand] = 0;
                runColumn[hand] = -1;

                continue;
            }

            int column = hand * 2 + (handMask == 0b10 ? 1 : 0);

            runLength[hand] = runLength[hand] > 0 && column != runColumn[hand] ? runLength[hand] + 1 : 1;
            runColumn[hand] = column;
        }
    }

    private static void writeMask(TrainingMeasure measure, int slot, int mask)
    {
        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((mask & (1 << column)) != 0)
                measure[slot, column] = SlotState.Note;
        }
    }

    private static int[] buildPairMasks()
    {
        var masks = new int[TrainingGrid.COLUMNS * (TrainingGrid.COLUMNS - 1) / 2];
        int count = 0;

        for (int a = 0; a < TrainingGrid.COLUMNS; a++)
        {
            for (int b = a + 1; b < TrainingGrid.COLUMNS; b++)
                masks[count++] = (1 << a) | (1 << b);
        }

        return masks;
    }

    /// <summary>마스크에 선 비트 중 하나를 균등하게 뽑는다.</summary>
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
