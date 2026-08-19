using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 22미니잭. 모든 이벤트가 2동치이고, <b>같은 2동치가 반드시 두 번씩 짝을 지어</b> 나온다.
/// 마디당 짝 8개 = 이벤트 16개 = 노트 32개.
/// </summary>
/// <remarks>
/// <para>
/// 짝 하나는 슬롯 <c>4k</c>와 <c>4k+2</c>에 같은 2동치를 놓는 것이다. 규칙은 둘뿐이다:
/// </para>
/// <code>
/// P_{k+1} ≠ P_k                     (같은 짝 2연속 = 4연타 금지)
/// P_{k-1} ∩ P_k ∩ P_{k+1} = ∅       (공유 컬럼의 6연타 금지)
/// </code>
/// <para>
/// 이웃한 두 짝이 한 컬럼을 공유하면 그 컬럼은 4연타가 되는데, 이건 허용한다(막으면 패턴이 굳는다).
/// 세 짝이 연달아 같은 컬럼을 물면 6연타가 되므로 그것만 막는다. 후보는 공유가 있을 때 3가지,
/// 서로소일 때 5가지라 <b>데드락이 없다.</b>
/// </para>
/// <para>
/// <b>컬럼 균등</b>: 짝 8개 × 2컬럼 = 16 컬럼슬롯이므로 <b>각 컬럼이 정확히 4개 짝</b>에 들어가면
/// 컬럼당 8노트가 된다(짝마다 2노트씩). K4의 변으로 보면 모든 꼭짓점의 차수가 4인 8변 멀티집합이며,
/// 6가지 짝 전부 + 상보 한 쌍이 그 예다. 닫힌 형태가 없어 할당량 가중 그리디 + 마디 재시도로 채운다.
/// </para>
/// </remarks>
public class MiniJack22Generator : IPatternGenerator
{
    private const int pair_slots = 4;

    private const int pairs_per_measure = TrainingGrid.SLOTS_PER_MEASURE / pair_slots;

    /// <summary>컬럼별 등장 짝 수. 8쌍 × 2컬럼 / 4 = 4.</summary>
    private const int column_quota = pairs_per_measure * 2 / TrainingGrid.COLUMNS;

    private const int max_attempts = 200;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        readPreviousPairs(previous, out int lastPair, out int secondLastPair);

        for (int attempt = 0; attempt < max_attempts; attempt++)
        {
            if (tryBuild(rng, lastPair, secondLastPair, true, out var measure))
                return measure;
        }

        tryBuild(rng, lastPair, secondLastPair, false, out var fallback);

        return fallback;
    }

    private static bool tryBuild(Random rng, int lastPair, int secondLastPair, bool enforceQuota, out TrainingMeasure measure)
    {
        var quota = new int[TrainingGrid.COLUMNS];

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            quota[column] = column_quota;

        measure = new TrainingMeasure();

        for (int k = 0; k < pairs_per_measure; k++)
        {
            int pair = pickPair(rng, quota, lastPair, secondLastPair, enforceQuota);

            if (pair == 0)
                return false;

            int slot = k * pair_slots;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if ((pair & (1 << column)) == 0)
                    continue;

                quota[column]--;
                measure[slot, column] = SlotState.Note;
                measure[slot + pair_slots / 2, column] = SlotState.Note;
            }

            secondLastPair = lastPair;
            lastPair = pair;
        }

        return true;
    }

    private static int pickPair(Random rng, int[] quota, int lastPair, int secondLastPair, bool enforceQuota)
    {
        var candidates = new int[PatternPicker.PairMasks.Length];
        var weights = new int[candidates.Length];
        int count = 0;

        foreach (int pair in PatternPicker.PairMasks)
        {
            if (pair == lastPair)
                continue;

            if ((pair & lastPair & secondLastPair) != 0)
                continue;

            int weight = 0;
            bool ok = true;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if ((pair & (1 << column)) == 0)
                    continue;

                if (enforceQuota && quota[column] <= 0)
                    ok = false;

                weight += quota[column];
            }

            if (!ok)
                continue;

            candidates[count] = pair;
            weights[count] = weight;
            count++;
        }

        return count == 0 ? 0 : PatternPicker.PickWeighted(rng, candidates, weights, count);
    }

    /// <summary>직전 마디의 마지막 두 짝. 모양이 어긋나면 제약 없이 시작한다.</summary>
    private static void readPreviousPairs(TrainingMeasure? previous, out int lastPair, out int secondLastPair)
    {
        lastPair = 0;
        secondLastPair = 0;

        if (previous == null)
            return;

        int last = previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 2);
        int secondLast = previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - pair_slots - 2);

        if (PatternPicker.BitCount(last) == 2)
            lastPair = last;

        if (PatternPicker.BitCount(secondLast) == 2)
            secondLastPair = secondLast;
    }
}
