using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 13미니잭. 3동치(슬롯 0,4,…,28)와 단노트(슬롯 2,6,…,30)가 2슬롯 간격으로 교대하고,
/// <b>단노트는 반드시 직전 3동치 안에서</b> 나와 따닥이 된다. 마디당 이벤트 16개 = 노트 32개.
/// </summary>
/// <remarks>
/// <para>
/// 3동치를 <c>¬a_k</c>, 단노트를 <c>s_k</c>라 하면 규칙은 <c>s_k ≠ a_k</c>다.
/// <b>4연타 금지</b>를 연속 4이벤트 창으로 풀면 조건이 하나만 더 붙는다:
/// </para>
/// <code>
/// s_k ≠ a_k          (단노트가 직전 3동치 안)
/// s_{k+1} ≠ s_k      (4연타 금지에서 나오는 유일한 조건)
/// </code>
/// <para>
/// <c>a</c> 수열에는 아무 제약이 없다(같은 3동치 두 번도 허용). 3연타 금지판에서는 <c>s_k</c>가
/// <c>a_{k+1}</c>로 완전히 강제됐는데, 4연타로 풀리면서 <b>단노트가 다음 3동치까지 살아남아
/// 3연타가 되는 것</b>이 허용된다 — 이게 미니잭의 본체다.
/// </para>
/// <para>
/// <b>컬럼 균등</b>: 컬럼 <c>j</c>의 노트 수는 <c>(8 − n_j) + σ_j</c>라 <c>σ_j = n_j</c>여야 8이 된다.
/// <c>a</c>와 <c>s</c>를 각각 컬럼당 2회로 두면 <b>컬럼당 정확히 8노트</b>가 된다.
/// <c>a_k</c>는 <b>단노트 후보가 남는 값 중에서만</b> 고르므로 재시도가 거의 걸리지 않는다.
/// </para>
/// </remarks>
public class MiniJack13Generator : IPatternGenerator
{
    private const int chord_interval_slots = 4;

    private const int chords_per_measure = TrainingGrid.SLOTS_PER_MEASURE / chord_interval_slots;

    /// <summary>컬럼별 3동치 제외 횟수이자 단노트 횟수. 8 / 4 = 2.</summary>
    private const int column_quota = chords_per_measure / TrainingGrid.COLUMNS;

    private const int max_attempts = 200;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        int previousSingle = readPreviousSingle(previous);

        for (int attempt = 0; attempt < max_attempts; attempt++)
        {
            if (tryBuild(rng, previousSingle, true, out var measure))
                return measure;
        }

        tryBuild(rng, previousSingle, false, out var fallback);

        return fallback;
    }

    private static bool tryBuild(Random rng, int previousSingle, bool enforceQuota, out TrainingMeasure measure)
    {
        var chordQuota = new int[TrainingGrid.COLUMNS];
        var singleQuota = new int[TrainingGrid.COLUMNS];

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            chordQuota[column] = column_quota;
            singleQuota[column] = column_quota;
        }

        measure = new TrainingMeasure();

        int lastSingle = previousSingle;

        for (int k = 0; k < chords_per_measure; k++)
        {
            int omitted = pickOmission(rng, chordQuota, singleQuota, lastSingle, enforceQuota);

            if (omitted < 0)
                return false;

            int single = pickSingle(rng, singleQuota, omitted, lastSingle, enforceQuota);

            if (single < 0)
                return false;

            chordQuota[omitted]--;
            singleQuota[single]--;

            int chordSlot = k * chord_interval_slots;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            {
                if (column != omitted)
                    measure[chordSlot, column] = SlotState.Note;
            }

            measure[chordSlot + chord_interval_slots / 2, single] = SlotState.Note;

            lastSingle = single;
        }

        return true;
    }

    /// <summary>3동치가 뺄 컬럼. 그것을 골랐을 때 단노트 후보가 남는 값만 고른다.</summary>
    private static int pickOmission(Random rng, int[] chordQuota, int[] singleQuota, int lastSingle, bool enforceQuota)
    {
        var candidates = new int[TrainingGrid.COLUMNS];
        var weights = new int[TrainingGrid.COLUMNS];
        int count = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if (enforceQuota && chordQuota[column] <= 0)
                continue;

            if (pickSingle(null, singleQuota, column, lastSingle, enforceQuota) < 0)
                continue;

            candidates[count] = column;
            weights[count] = chordQuota[column];
            count++;
        }

        return count == 0 ? -1 : PatternPicker.PickWeighted(rng, candidates, weights, count);
    }

    /// <summary><paramref name="rng"/>가 <c>null</c>이면 후보 존재 여부만 확인한다(0 이상이면 있음).</summary>
    private static int pickSingle(Random? rng, int[] quota, int omitted, int lastSingle, bool enforceQuota)
    {
        var candidates = new int[TrainingGrid.COLUMNS];
        var weights = new int[TrainingGrid.COLUMNS];
        int count = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if (column == omitted || column == lastSingle)
                continue;

            if (enforceQuota && quota[column] <= 0)
                continue;

            candidates[count] = column;
            weights[count] = quota[column];
            count++;
        }

        if (count == 0)
            return -1;

        return rng == null ? candidates[0] : PatternPicker.PickWeighted(rng, candidates, weights, count);
    }

    private static int readPreviousSingle(TrainingMeasure? previous)
    {
        if (previous == null)
            return -1;

        return PatternPicker.OnlyColumn(previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 2));
    }
}
