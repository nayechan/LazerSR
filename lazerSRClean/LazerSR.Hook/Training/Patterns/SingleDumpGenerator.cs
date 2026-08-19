using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 싱글덤프. 매 슬롯(32분음표)마다 노트가 하나씩, 컬럼은 무작위로 흩어진다.
/// <para>규칙:</para>
/// <list type="number">
/// <item>모든 슬롯에 노트가 정확히 하나 (마디당 32노트).</item>
/// <item><b>직전 2개 노트와 같은 컬럼은 불가.</b> 마디 경계를 넘어서도 적용되므로 이 패턴은
/// <c>previous</c>를 실제로 사용한다.</item>
/// <item>마디 안에서 각 컬럼이 정확히 8번씩 등장하도록 표준화.</item>
/// </list>
/// <para>
/// 규칙 2를 "슬롯 간격 4 이상"(= 사이에 노트 3개)으로 잡으면 패턴이 완전히 고정된다 —
/// 연속 4슬롯이 항상 4컬럼 전부가 되어 <c>s[i+4] = s[i]</c>가 강제되고, 마디가 순열 하나의
/// 8회 반복(총 24가지)으로 굳는다. 그래서 <b>직전 2개</b>가 정확한 기준이다.
/// </para>
/// <para>
/// 24코드잭과 달리 닫힌 형태가 없다. 남은 할당량으로 가중치를 준 그리디로 채우되,
/// 마디 끝에서 막히면(남은 할당량이 있는 컬럼이 하필 직전 2개에 들어있는 경우) 마디를 통째로
/// 다시 만든다. 마디 하나 생성 비용이 무시할 수준이라 재시도가 문제되지 않는다.
/// </para>
/// </summary>
public class SingleDumpGenerator : IPatternGenerator
{
    private const int notes_per_column = TrainingGrid.SLOTS_PER_MEASURE / TrainingGrid.COLUMNS;

    private const int max_attempts = 50;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        TrainingMeasure.ReadTailMasks(previous, out int maskPrev, out int maskPrevPrev);

        for (int attempt = 0; attempt < max_attempts; attempt++)
        {
            int[]? columns = tryBuild(rng, maskPrev, maskPrevPrev);

            if (columns != null)
                return toMeasure(columns);
        }

        return toMeasure(fallback(maskPrev, maskPrevPrev));
    }

    /// <summary>성공하면 슬롯별 컬럼 배열, 도중에 막히면 <c>null</c>.</summary>
    private static int[]? tryBuild(Random rng, int maskPrev, int maskPrevPrev)
    {
        var columns = new int[TrainingGrid.SLOTS_PER_MEASURE];
        var remaining = new int[TrainingGrid.COLUMNS];

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
            remaining[column] = notes_per_column;

        for (int slot = 0; slot < columns.Length; slot++)
        {
            int forbidden = maskPrev | maskPrevPrev;
            int chosen = pick(rng, remaining, forbidden);

            if (chosen < 0)
                return null;

            columns[slot] = chosen;
            remaining[chosen]--;

            maskPrevPrev = maskPrev;
            maskPrev = 1 << chosen;
        }

        return columns;
    }

    /// <summary>남은 할당량에 비례해 뽑는다. 편중된 컬럼이 먼저 소진되어 막힐 확률이 줄어든다.</summary>
    private static int pick(Random rng, int[] remaining, int forbidden)
    {
        int total = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((forbidden & (1 << column)) != 0)
                continue;

            total += remaining[column];
        }

        if (total <= 0)
            return -1;

        int roll = rng.Next(total);

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((forbidden & (1 << column)) != 0)
                continue;

            roll -= remaining[column];

            if (roll < 0)
                return column;
        }

        return -1;
    }

    /// <summary>
    /// 재시도가 전부 실패했을 때의 최후 수단. 주기 4 배열은 규칙(간격 4 ≥ 3)과 균등 분배를
    /// 항상 만족하므로, 첫 두 컬럼만 직전 노트와 겹치지 않게 고르면 된다.
    /// </summary>
    private static int[] fallback(int maskPrev, int maskPrevPrev)
    {
        for (int first = 0; first < TrainingGrid.COLUMNS; first++)
        {
            if (((maskPrev | maskPrevPrev) & (1 << first)) != 0)
                continue;

            for (int second = 0; second < TrainingGrid.COLUMNS; second++)
            {
                if (second == first || (maskPrev & (1 << second)) != 0)
                    continue;

                var cycle = new int[TrainingGrid.COLUMNS];
                cycle[0] = first;
                cycle[1] = second;

                int index = 2;

                for (int column = 0; column < TrainingGrid.COLUMNS; column++)
                {
                    if (column != first && column != second)
                        cycle[index++] = column;
                }

                var columns = new int[TrainingGrid.SLOTS_PER_MEASURE];

                for (int slot = 0; slot < columns.Length; slot++)
                    columns[slot] = cycle[slot % TrainingGrid.COLUMNS];

                return columns;
            }
        }

        // 도달 불가 — 막을 수 있는 컬럼이 최대 2개라 위 루프가 반드시 성공한다.
        var identity = new int[TrainingGrid.SLOTS_PER_MEASURE];

        for (int slot = 0; slot < identity.Length; slot++)
            identity[slot] = slot % TrainingGrid.COLUMNS;

        return identity;
    }

    private static TrainingMeasure toMeasure(int[] columns)
    {
        var measure = new TrainingMeasure();

        for (int slot = 0; slot < columns.Length; slot++)
            measure[slot, columns[slot]] = SlotState.Note;

        return measure;
    }
}
