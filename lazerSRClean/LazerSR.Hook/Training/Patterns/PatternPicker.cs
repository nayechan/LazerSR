using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// "제약이 걸린 균형 수열"로 환원되는 세부패턴들이 공유하는 추첨 도구.
/// <para>
/// 23코드잭 · 13미니잭 · 12미니잭 · 22미니잭이 같은 골격(할당량 가중 그리디 + 마디 재시도)을 쓴다.
/// 기존 생성기들은 각자 닫힌 형태로 풀려 이 도구가 필요 없으므로 건드리지 않는다.
/// </para>
/// </summary>
internal static class PatternPicker
{
    internal const int AllColumnsMask = (1 << TrainingGrid.COLUMNS) - 1;

    /// <summary>가능한 2동치 6가지를 컬럼 비트마스크로.</summary>
    internal static readonly int[] PairMasks = buildPairMasks();

    internal static int BitCount(int mask)
    {
        int count = 0;

        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }

        return count;
    }

    /// <summary>비트가 정확히 하나 선 마스크의 컬럼. 아니면 −1.</summary>
    internal static int OnlyColumn(int mask)
    {
        if (BitCount(mask) != 1)
            return -1;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((mask & (1 << column)) != 0)
                return column;
        }

        return -1;
    }

    internal static int PickFromMask(Random rng, int mask)
    {
        int target = rng.Next(BitCount(mask));

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((mask & (1 << column)) == 0)
                continue;

            if (target-- == 0)
                return column;
        }

        return 0;
    }

    /// <summary>남은 할당량에 비례해 뽑는다. 총합이 0이면 균등하게 뽑는다.</summary>
    internal static int PickWeighted(Random rng, int[] values, int[] weights, int count)
    {
        int total = 0;

        for (int i = 0; i < count; i++)
            total += weights[i];

        if (total <= 0)
            return values[rng.Next(count)];

        int target = rng.Next(total);

        for (int i = 0; i < count; i++)
        {
            target -= weights[i];

            if (target < 0)
                return values[i];
        }

        return values[count - 1];
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
}
