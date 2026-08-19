using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 23코드잭. 3동치(슬롯 0,4,…,28)와 2동치(슬롯 2,6,…,30)가 2슬롯 간격으로 교대한다.
/// 마디당 이벤트 16개 = 노트 40개.
/// </summary>
/// <remarks>
/// <para>
/// 3동치를 <c>¬a_k</c>(컬럼 <c>a_k</c>를 뺀 것), 2동치를 <c>B_k</c>라 한다. <b>4연타 금지</b>를
/// 연속 4이벤트 창(<c>3,2,3,2</c>와 <c>2,3,2,3</c> 두 종류)으로 풀면 제약이 이것 하나로 압축된다:
/// </para>
/// <code>
/// B_{k-1} ∩ B_k ⊆ {a_k}
/// </code>
/// <para>
/// 엄밀히는 <c>a_{k-1} = a_{k+1}</c>일 때 <c>a_{k-1}</c>까지 허용되지만, 그러면 다음 마디를 미리
/// 알아야 하므로 <b>보수적으로 <c>{a_k}</c>만 허용</b>한다. 규칙을 어기지 않고 자유도만 조금 잃는다.
/// </para>
/// <para>
/// 3연타 금지판과 달리 <b>컬럼 하나가 2동치를 관통해 3연타</b>할 수 있다 — 이게 코드잭의 본체다.
/// 대신 <c>B</c>가 강제되지 않아 <c>a</c> 수열도 자유로워지고(같은 3동치 두 번도 허용) 변형이 크게 는다.
/// </para>
/// <para>
/// <b>컬럼 균등</b>: 컬럼 <c>j</c>의 노트 수는 <c>(8 − n_j) + m_j</c>이므로,
/// <c>a</c>를 컬럼당 2회 · <c>B</c>를 컬럼당 4회로 두면 <b>컬럼당 정확히 10노트</b>가 된다.
/// 닫힌 형태가 없어 할당량 가중 그리디로 채우고, 막히면 마디를 통째로 재시도한다.
/// <c>a_k</c>를 고를 때 <b>직전 2동치 안에 있는 값을 우선</b>하면 다음 <c>B</c>의 후보가
/// 1개가 아니라 3개로 유지되어 재시도가 거의 걸리지 않는다.
/// </para>
/// </remarks>
public class ChordJack23Generator : IPatternGenerator
{
    private const int chord_interval_slots = 4;

    private const int chords_per_measure = TrainingGrid.SLOTS_PER_MEASURE / chord_interval_slots;

    /// <summary>컬럼별 3동치 제외 횟수. 8 / 4 = 2.</summary>
    private const int chord_quota = chords_per_measure / TrainingGrid.COLUMNS;

    /// <summary>컬럼별 2동치 등장 횟수. 8쌍 × 2컬럼 / 4 = 4.</summary>
    private const int pair_quota = chords_per_measure * 2 / TrainingGrid.COLUMNS;

    private const int max_attempts = 200;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        int previousPair = readPreviousPair(previous);

        for (int attempt = 0; attempt < max_attempts; attempt++)
        {
            if (tryBuild(rng, previousPair, true, out var measure))
                return measure;
        }

        // 할당량을 포기해도 제약만으로는 후보가 항상 남으므로 여기서 반드시 성공한다.
        tryBuild(rng, previousPair, false, out var fallback);

        return fallback;
    }

    private static bool tryBuild(Random rng, int previousPair, bool enforceQuota, out TrainingMeasure measure)
    {
        var chordQuota = new int[TrainingGrid.COLUMNS];
        var pairQuota = new int[TrainingGrid.COLUMNS];

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            chordQuota[column] = chord_quota;
            pairQuota[column] = pair_quota;
        }

        measure = new TrainingMeasure();

        int lastPair = previousPair;

        for (int k = 0; k < chords_per_measure; k++)
        {
            int omitted = pickOmission(rng, chordQuota, lastPair, enforceQuota);

            if (omitted < 0)
                return false;

            int pair = pickPair(rng, pairQuota, lastPair, omitted, enforceQuota);

            if (pair == 0)
                return false;

            chordQuota[omitted]--;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if ((pair & (1 << column)) != 0)
                    pairQuota[column]--;
            }

            int chordSlot = k * chord_interval_slots;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if (column != omitted)
                    measure[chordSlot, column] = SlotState.Note;

                if ((pair & (1 << column)) != 0)
                    measure[chordSlot + chord_interval_slots / 2, column] = SlotState.Note;
            }

            lastPair = pair;
        }

        return true;
    }

    /// <summary>
    /// 3동치가 뺄 컬럼. 다음 2동치의 후보를 3개로 유지하려면 <b>직전 2동치 안</b>에서 골라야 하므로
    /// 그쪽을 우선하고, 할당량이 없으면 나머지로 넘어간다.
    /// </summary>
    private static int pickOmission(Random rng, int[] quota, int lastPair, bool enforceQuota)
    {
        var preferred = new int[TrainingGrid.COLUMNS];
        var preferredWeights = new int[TrainingGrid.COLUMNS];
        var others = new int[TrainingGrid.COLUMNS];
        var otherWeights = new int[TrainingGrid.COLUMNS];
        int preferredCount = 0;
        int otherCount = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if (enforceQuota && quota[column] <= 0)
                continue;

            if (lastPair != 0 && (lastPair & (1 << column)) != 0)
            {
                preferred[preferredCount] = column;
                preferredWeights[preferredCount] = quota[column];
                preferredCount++;
            }
            else
            {
                others[otherCount] = column;
                otherWeights[otherCount] = quota[column];
                otherCount++;
            }
        }

        if (preferredCount > 0)
            return PatternPicker.PickWeighted(rng, preferred, preferredWeights, preferredCount);

        return otherCount > 0 ? PatternPicker.PickWeighted(rng, others, otherWeights, otherCount) : -1;
    }

    private static int pickPair(Random rng, int[] quota, int lastPair, int omitted, bool enforceQuota)
    {
        var candidates = new int[PatternPicker.PairMasks.Length];
        var weights = new int[candidates.Length];
        int count = 0;

        foreach (int pair in PatternPicker.PairMasks)
        {
            // B_{k-1} ∩ B_k ⊆ {a_k}
            if ((pair & lastPair & ~(1 << omitted)) != 0)
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

    /// <summary>직전 마디의 마지막 2동치. 모양이 어긋나면 제약 없이 시작한다.</summary>
    private static int readPreviousPair(TrainingMeasure? previous)
    {
        if (previous == null)
            return 0;

        int mask = previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 2);

        return PatternPicker.BitCount(mask) == 2 ? mask : 0;
    }
}
