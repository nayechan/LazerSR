using System;
using System.Collections.Generic;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 24코드잭. 4동치와 2동치가 2슬롯(16분음표) 간격으로 교대한다.
/// <para>
/// 4동치는 슬롯 0,4,8,…,28에 고정 배치되고, 2동치는 슬롯 2,6,10,…,30에 난수로 배치된다.
/// 마디당 이벤트 16개 = 노트 48개.
/// </para>
/// <para>
/// 2동치 컬럼 선택은 <b>마디 내 균형을 강제</b>한다 — 8개의 2동치가 각 컬럼을 정확히 4번씩 쓰므로
/// 모든 컬럼이 매 마디 똑같이 12노트를 받는다. 이 조건을 풀면 <b>상보 쌍끼리 개수가 같아야 한다</b>는
/// 성질만 남는다({01}={23}, {02}={13}, {03}={12}). 세 값의 합이 4이고, 가능한 배열은 44,730가지다.
/// </para>
/// </summary>
public class ChordJack24Generator : IPatternGenerator
{
    /// <summary>4동치끼리의 간격. 그 중간(2슬롯)에 2동치가 들어간다.</summary>
    private const int chord_interval_slots = 4;

    /// <summary>마디당 4동치 개수이자 2동치 개수. 32 / 4 = 8.</summary>
    private const int chords_per_measure = TrainingGrid.SLOTS_PER_MEASURE / chord_interval_slots;

    /// <summary>상보 쌍 3조. 조마다 앞뒤 두 쌍이 서로의 여집합이다. 0조는 같은 손, 1·2조는 양손.</summary>
    private static readonly int[][] complementary_pairs =
    {
        new[] { 0, 1 }, new[] { 2, 3 },
        new[] { 0, 2 }, new[] { 1, 3 },
        new[] { 0, 3 }, new[] { 1, 2 },
    };

    private static readonly int[][] balanced_counts;
    private static readonly double[] balanced_weights;
    private static readonly double balanced_weight_total;

    static ChordJack24Generator()
    {
        // 조별 개수 (a, b, c), a + b + c = 8 / 2. 배열 가짓수 8! / (a!² b!² c!²)에 비례해 뽑아야
        // 44,730가지 전체에 대해 균등해진다.
        int half = chords_per_measure / 2;
        var counts = new List<int[]>();
        var weights = new List<double>();

        for (int a = 0; a <= half; a++)
        {
            for (int b = 0; a + b <= half; b++)
            {
                int c = half - a - b;
                counts.Add(new[] { a, b, c });
                weights.Add(factorial(chords_per_measure) / (square(factorial(a)) * square(factorial(b)) * square(factorial(c))));
            }
        }

        balanced_counts = counts.ToArray();
        balanced_weights = weights.ToArray();

        double total = 0;
        foreach (double w in balanced_weights)
            total += w;
        balanced_weight_total = total;
    }

    /// <summary>마디 경계가 항상 결정론적(2동치 → 4동치, 간격 고정)이라 <paramref name="previous"/>를 보지 않는다.</summary>
    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        var measure = new TrainingMeasure();
        List<int[]> doubles = buildDoubles(rng);

        for (int i = 0; i < chords_per_measure; i++)
        {
            int quadSlot = i * chord_interval_slots;
            int doubleSlot = quadSlot + chord_interval_slots / 2;

            for (int column = 0; column < TrainingGrid.COLUMNS; column++)
                measure[quadSlot, column] = SlotState.Note;

            int[] pair = doubles[i];
            measure[doubleSlot, pair[0]] = SlotState.Note;
            measure[doubleSlot, pair[1]] = SlotState.Note;
        }

        return measure;
    }

    /// <summary>균형 조건을 만족하는 2동치 8개를 뽑아 무작위 순서로 돌려준다.</summary>
    private static List<int[]> buildDoubles(Random rng)
    {
        int[] counts = pickBalancedCounts(rng);
        var bag = new List<int[]>(chords_per_measure);

        for (int group = 0; group < counts.Length; group++)
        {
            // 조 안의 두 쌍을 같은 횟수로 넣는 것이 곧 균형 조건이다.
            for (int i = 0; i < counts[group]; i++)
            {
                bag.Add(complementary_pairs[group * 2]);
                bag.Add(complementary_pairs[group * 2 + 1]);
            }
        }

        for (int i = bag.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        return bag;
    }

    private static int[] pickBalancedCounts(Random rng)
    {
        double remaining = rng.NextDouble() * balanced_weight_total;

        for (int i = 0; i < balanced_counts.Length; i++)
        {
            remaining -= balanced_weights[i];
            if (remaining <= 0)
                return balanced_counts[i];
        }

        return balanced_counts[balanced_counts.Length - 1];
    }

    private static double factorial(int n)
    {
        double result = 1;
        for (int i = 2; i <= n; i++)
            result *= i;
        return result;
    }

    private static double square(double x) => x * x;
}
