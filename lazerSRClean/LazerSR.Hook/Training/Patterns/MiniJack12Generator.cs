using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 12미니잭. 2동치(슬롯 0,4,…,28)와 단노트(슬롯 2,6,…,30)가 2슬롯 간격으로 교대하고,
/// <b>단노트는 반드시 직전 2동치 안에서</b> 나와 따닥이 된다. 마디당 이벤트 16개 = 노트 24개.
/// </summary>
/// <remarks>
/// <para>
/// 2동치를 <c>P_k = {s_k, t_k}</c>(따닥이 되는 <c>s_k</c>와 짝꿍 <c>t_k</c>)라 하면 3연타 금지는
/// <c>s_k ∉ P_{k+1}</c>이고, 이는 아래 둘로 갈린다:
/// </para>
/// <code>
/// s_{k+1} ≠ s_k
/// t_{k+1} ≠ s_k
/// </code>
/// <para>
/// <b>연타 컬럼을 균등하게</b>(<c>s</c>를 컬럼당 2회) 두면 좋은 성질이 따라온다 —
/// <c>P_k</c>가 <c>s_k</c>를 포함하면서 <c>s_{k-1}</c>은 포함하면 안 되므로 <b><c>s_k = s_{k-1}</c>이
/// 애초에 모순</b>이다. 인접 중복 금지가 규칙 없이 저절로 성립한다.
/// </para>
/// <para>
/// <c>t</c>까지 컬럼당 2회로 두면 <b>컬럼당 정확히 6노트</b>가 된다(<c>s</c>는 이벤트 2개에,
/// <c>t</c>는 1개에 나오므로 <c>2σ_j + τ_j = 6</c>). <c>t_k</c>의 후보는 <c>{s_{k-1}, s_k}</c>를 뺀
/// 2가지라 데드락은 없고, 할당량이 얹힐 때만 마디 재시도가 걸린다.
/// </para>
/// </remarks>
public class MiniJack12Generator : IPatternGenerator
{
    private const int event_interval_slots = 4;

    private const int pairs_per_measure = TrainingGrid.SLOTS_PER_MEASURE / event_interval_slots;

    /// <summary>컬럼별 연타 횟수이자 짝꿍 횟수. 8 / 4 = 2.</summary>
    private const int column_quota = pairs_per_measure / TrainingGrid.COLUMNS;

    private const int max_attempts = 200;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        int previousSingle = previous == null
            ? -1
            : PatternPicker.OnlyColumn(previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 2));

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
        var singleQuota = new int[TrainingGrid.COLUMNS];
        var partnerQuota = new int[TrainingGrid.COLUMNS];

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            singleQuota[column] = column_quota;
            partnerQuota[column] = column_quota;
        }

        measure = new TrainingMeasure();

        int lastSingle = previousSingle;

        for (int k = 0; k < pairs_per_measure; k++)
        {
            int single = pickColumn(rng, singleQuota, lastSingle, -1, enforceQuota);

            if (single < 0)
                return false;

            int partner = pickColumn(rng, partnerQuota, lastSingle, single, enforceQuota);

            if (partner < 0)
                return false;

            singleQuota[single]--;
            partnerQuota[partner]--;

            int slot = k * event_interval_slots;

            measure[slot, single] = SlotState.Note;
            measure[slot, partner] = SlotState.Note;
            measure[slot + event_interval_slots / 2, single] = SlotState.Note;

            lastSingle = single;
        }

        return true;
    }

    /// <summary>두 컬럼을 배제하고 남은 것 중 남은 할당량에 비례해 뽑는다.</summary>
    private static int pickColumn(Random rng, int[] quota, int excludedA, int excludedB, bool enforceQuota)
    {
        var candidates = new int[TrainingGrid.COLUMNS];
        var weights = new int[TrainingGrid.COLUMNS];
        int count = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if (column == excludedA || column == excludedB)
                continue;

            if (enforceQuota && quota[column] <= 0)
                continue;

            candidates[count] = column;
            weights[count] = quota[column];
            count++;
        }

        return count == 0 ? -1 : PatternPicker.PickWeighted(rng, candidates, weights, count);
    }
}
