using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 덴시덤프. 싱글덤프에서 밀도를 올린 변형이다.
/// <list type="number">
/// <item>4슬롯마다(0,4,…,28) <b>2컬럼 동시치기</b>, 나머지 슬롯은 단노트 → 마디당 40노트.</item>
/// <item>바로 옆 슬롯(간격 1)에 같은 컬럼은 <b>항상 금지</b>.</item>
/// <item>간격 2(<c>1컬럼/k/1컬럼</c>)는 <b>8슬롯 구간마다 정확히 1회</b> 허용 — 마디당 4회.
/// 동시치기의 컬럼도 이 판정에 포함된다.</item>
/// <item>그 외에는 싱글덤프와 같이 간격 3 이상.</item>
/// <item>컬럼별 등장 횟수 균등(40 / 4 = 10회)을 목표로 한다.</item>
/// </list>
/// <para>
/// <b>규칙 1만으로 패턴이 거의 고정된다.</b> 동시치기를 포함한 3슬롯 창은 노트가 4개(2+1+1)라
/// 간격 3 규칙 아래에서는 항상 4컬럼 전부가 되어 포화된다. 그래서
/// <c>동시치기(4m) = 전체 − { s[4m+1], s[4m+2] }</c>로 완전히 결정되고, 자유로운 선택은
/// 슬롯 3(mod 4)과 간격 2를 소비하는 자리에만 남는다. <b>규칙 3이 이 패턴의 주된 랜덤성 공급원</b>이다.
/// </para>
/// <para>
/// 자유도가 좁아 컬럼 균등을 엄격히 강제하면 마디가 아예 안 만들어지는 경우가 있다. 그래서
/// <b>엄격 → 완화 → 최후</b> 3단계로 시도한다 — 간격 규칙과 구간별 간격 2 횟수는 언제나 지키고,
/// 컬럼 균등만 단계적으로 느슨해진다.
/// </para>
/// </summary>
public class DenseDumpGenerator : IPatternGenerator
{
    /// <summary>동시치기가 놓이는 간격.</summary>
    private const int chord_interval_slots = 4;

    /// <summary>간격 2를 정확히 한 번씩 배정할 구간 길이.</summary>
    private const int region_slots = 8;

    private const int regions = TrainingGrid.SLOTS_PER_MEASURE / region_slots;

    private const int chord_slots = TrainingGrid.SLOTS_PER_MEASURE / chord_interval_slots;

    private const int notes_per_measure = TrainingGrid.SLOTS_PER_MEASURE + chord_slots;

    private const int notes_per_column = notes_per_measure / TrainingGrid.COLUMNS;

    private const int all_columns_mask = (1 << TrainingGrid.COLUMNS) - 1;

    private const int max_attempts = 200;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        TrainingMeasure.ReadTailMasks(previous, out int maskPrev, out int maskPrevPrev);

        // 1단계: 컬럼 균등을 정확히 맞춘다.
        var slots = attempt(rng, maskPrev, maskPrevPrev, strictColumns: true, enforceRegions: true);

        // 2단계: 균등을 목표로만 두고 초과를 허용한다 (편차는 보통 ±1).
        slots ??= attempt(rng, maskPrev, maskPrevPrev, strictColumns: false, enforceRegions: true);

        // 3단계: 구간별 간격 2 배정까지 포기. 간격 규칙만 지키므로 항상 성립한다.
        slots ??= attempt(rng, maskPrev, maskPrevPrev, strictColumns: false, enforceRegions: false);

        return toMeasure(slots ?? throw new InvalidOperationException("덴시덤프 마디 생성 실패"));
    }

    private static int[][]? attempt(Random rng, int maskPrev, int maskPrevPrev, bool strictColumns, bool enforceRegions)
    {
        for (int i = 0; i < max_attempts; i++)
        {
            int[][]? built = tryBuild(rng, maskPrev, maskPrevPrev, strictColumns, enforceRegions);

            if (built != null)
                return built;
        }

        return null;
    }

    /// <summary>성공하면 슬롯별 컬럼 배열, 도중에 막히면 <c>null</c>.</summary>
    private static int[][]? tryBuild(Random rng, int maskPrev, int maskPrevPrev, bool strictColumns, bool enforceRegions)
    {
        var slotColumns = new int[TrainingGrid.SLOTS_PER_MEASURE][];
        var used = new int[TrainingGrid.COLUMNS];
        var regionSpent = new bool[regions];

        for (int slot = 0; slot < slotColumns.Length; slot++)
        {
            int region = slot / region_slots;
            int slotsLeftInRegion = region_slots - slot % region_slots;
            bool regionNeedsGap2 = enforceRegions && !regionSpent[region];
            bool isChord = slot % chord_interval_slots == 0;

            // 구간 안에서 간격 2 자리가 고르게 흩어지도록, 남은 슬롯 수의 역수 확률로 고른다.
            // 구간 마지막 슬롯이면 무조건 여기서 써야 한다.
            bool wantSpend = regionNeedsGap2 && (slotsLeftInRegion == 1 || rng.Next(slotsLeftInRegion) == 0);

            int[]? chosen = null;
            bool spent = false;

            if (wantSpend)
            {
                chosen = pickSlot(rng, used, maskPrev, maskPrevPrev, isChord, spend: true, strictColumns);
                spent = chosen != null;
            }

            if (chosen == null)
            {
                // 마지막 기회였는데 못 썼으면 이 마디는 구간 조건을 만족시킬 수 없다.
                if (regionNeedsGap2 && slotsLeftInRegion == 1)
                    return null;

                chosen = pickSlot(rng, used, maskPrev, maskPrevPrev, isChord, spend: false, strictColumns);
            }

            if (chosen == null)
                return null;

            if (spent)
                regionSpent[region] = true;

            slotColumns[slot] = chosen;

            int mask = 0;

            foreach (int column in chosen)
            {
                used[column]++;
                mask |= 1 << column;
            }

            maskPrevPrev = maskPrev;
            maskPrev = mask;
        }

        return slotColumns;
    }

    /// <summary>
    /// 슬롯 하나에 놓을 컬럼을 고른다. <paramref name="spend"/>가 true면 컬럼 하나를 일부러
    /// 2슬롯 전(<paramref name="softMask"/>)과 겹치게 잡아 간격 2를 한 번 만든다.
    /// </summary>
    private static int[]? pickSlot(Random rng, int[] used, int hardMask, int softMask, bool isChord, bool spend, bool strictColumns)
    {
        int freeMask = all_columns_mask & ~hardMask & ~softMask;

        if (!spend)
        {
            int first = weightedPick(rng, used, freeMask, strictColumns);

            if (first < 0)
                return null;

            if (!isChord)
                return new[] { first };

            int second = weightedPick(rng, used, freeMask & ~(1 << first), strictColumns);

            return second < 0 ? null : new[] { first, second };
        }

        // 간격 2는 슬롯당 최대 한 컬럼만 만든다 — 동시치기에서 둘 다 겹치면 한 슬롯에 2회가 되어버린다.
        int repeated = weightedPick(rng, used, softMask & ~hardMask, strictColumns);

        if (repeated < 0)
            return null;

        if (!isChord)
            return new[] { repeated };

        int partner = weightedPick(rng, used, freeMask & ~(1 << repeated), strictColumns);

        return partner < 0 ? null : new[] { repeated, partner };
    }

    /// <summary>
    /// 후보 컬럼 중 하나를 남은 할당량에 비례해 뽑는다.
    /// <paramref name="strictColumns"/>가 true면 할당량을 다 쓴 컬럼은 아예 제외하고,
    /// false면 가중치 1로 남겨 균등을 목표로만 삼는다.
    /// </summary>
    private static int weightedPick(Random rng, int[] used, int candidateMask, bool strictColumns)
    {
        int total = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((candidateMask & (1 << column)) == 0)
                continue;

            total += weightOf(used[column], strictColumns);
        }

        if (total <= 0)
            return -1;

        int roll = rng.Next(total);

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((candidateMask & (1 << column)) == 0)
                continue;

            roll -= weightOf(used[column], strictColumns);

            if (roll < 0)
                return column;
        }

        return -1;
    }

    private static int weightOf(int usedCount, bool strictColumns)
    {
        int remaining = notes_per_column - usedCount;

        if (remaining > 0)
            return remaining;

        return strictColumns ? 0 : 1;
    }

    private static TrainingMeasure toMeasure(int[][] slotColumns)
    {
        var measure = new TrainingMeasure();

        for (int slot = 0; slot < slotColumns.Length; slot++)
        {
            foreach (int column in slotColumns[slot])
                measure[slot, column] = SlotState.Note;
        }

        return measure;
    }
}
