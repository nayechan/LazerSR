using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 드르륵테크. 매 슬롯 단노트 하나(마디당 32노트)를 4슬롯 그룹으로 끊고,
/// <b>그룹의 뒤쪽 두 슬롯을 언제나 따닥(같은 컬럼 2연타)</b>으로 채운다.
/// </summary>
/// <remarks>
/// <para>
/// 그룹 하나는 <c>p, q, c, c</c>다. 기반 규칙은 <b>드르륵이 아니라 싱글덤프</b>를 계승한다 —
/// 인접 컬럼 롤을 요구하지 않고 간격 3만 지킨다. 롤을 계승하면 따닥 컬럼이 컬럼 1·4일 때
/// <c>q</c>가 벽 쪽으로 하나만 남아 흡수 상태가 생기고 균등분배가 불가능해진다.
/// </para>
/// <code>
/// p ≠ c_{m-1}          (직전 따닥이 2슬롯을 차지하므로 제약이 이것 하나로 줄어든다)
/// q ∉ {p, c_{m-1}}
/// c ∉ {p, q}
/// </code>
/// <para>
/// <b>따닥은 마디 안에서 컬럼당 정확히 2회</b>이고, <b>같은 컬럼이 연속 그룹에 오지 않는다.</b>
/// 이 두 조건 덕에 <c>c_{m-1} ≠ c_m</c>이 항상 성립하고, 그러면 <c>{p, q}</c>가
/// <c>{c_{m-1}, c_m}</c>의 여집합으로 <b>강제</b>되어 순서만 남는다. 그 결과 컬럼 <c>j</c>는
/// 자기가 따닥인 그룹과 그 다음 그룹에서만 <c>p/q</c>에서 빠지므로 <b>전체 컬럼 균등(컬럼당 8노트)이
/// 저절로 따라온다</b> — 싱글덤프가 그리디+재시도로 맞추던 것이 여기서는 구조로 나온다.
/// 단 마디 단위로는 정확히 8이 아니다. 마디 마지막 따닥은 블록을 다음 마디로 넘기므로 그 컬럼이
/// 9노트, 직전 마디에서 넘어온 컬럼이 7노트가 된다(둘이 같으면 상쇄되어 완전균등, 실측 28%).
/// 스트림 전체로는 오차가 0으로 수렴한다.
/// </para>
/// <para>
/// 금지 전이가 없어 따닥 수열은 멀티셋 <c>{0,0,1,1,2,2,3,3}</c>의 배열 중 <b>인접 중복만 없으면
/// 전부 유효</b>하다(2,520 중 864). 어떤 상태에서도 <c>(p,q)</c>가 최소 하나 남으므로 데드락이 없다.
/// </para>
/// <para>
/// 같은 컬럼 3연타는 구조적으로 불가능하다 — <c>q ≠ c</c>와 다음 그룹의 <c>p ≠ c</c>가 둘 다 강제된다.
/// 직전 따닥 컬럼만 마디 경계 너머로 들고 간다.
/// </para>
/// </remarks>
public class RollTechGenerator : IPatternGenerator
{
    private const int group_slots = 4;

    private const int groups_per_measure = TrainingGrid.SLOTS_PER_MEASURE / group_slots;

    /// <summary>컬럼별 따닥 횟수. 8 / 4 = 2.</summary>
    private const int jacks_per_column = groups_per_measure / TrainingGrid.COLUMNS;

    /// <summary>수열 재추첨 상한. 무작위 배열이 통과할 확률이 약 1/4라 도달할 일이 없다.</summary>
    private const int max_shuffle_attempts = 200;

    private int lastJack = -1;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        // 직전 마디를 우리가 만들었으면 마지막 두 슬롯이 같은 컬럼(따닥)이어야 한다.
        bool continuous = previous != null
                          && lastJack >= 0
                          && previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 2) == 1 << lastJack
                          && previous.ColumnMask(TrainingGrid.SLOTS_PER_MEASURE - 1) == 1 << lastJack;

        // 다른 패턴이 직전에 왔더라도 간격 3은 지킨다.
        TrainingMeasure.ReadTailMasks(previous, out int lastMask, out int secondLastMask);

        int[] jacks = buildJackSequence(rng, continuous ? lastJack : -1);
        var measure = new TrainingMeasure();

        for (int m = 0; m < groups_per_measure; m++)
        {
            int slot = m * group_slots;
            int jack = jacks[m];

            int avoidFirst = m == 0 ? lastMask | secondLastMask : 1 << jacks[m - 1];
            int avoidSecond = m == 0 ? lastMask : 1 << jacks[m - 1];

            pickLead(rng, avoidFirst, avoidSecond, jack, out int first, out int second);

            measure[slot, first] = SlotState.Note;
            measure[slot + 1, second] = SlotState.Note;
            measure[slot + 2, jack] = SlotState.Note;
            measure[slot + 3, jack] = SlotState.Note;
        }

        lastJack = jacks[groups_per_measure - 1];

        return measure;
    }

    /// <summary>
    /// 그룹 앞쪽 두 슬롯. 정상 흐름에서는 후보가 순서만 다른 2가지지만, 다른 패턴이 직전에 온
    /// 마디 첫 그룹에서는 더 넓어질 수 있어 조합을 전부 훑는다.
    /// </summary>
    private static void pickLead(Random rng, int avoidFirst, int avoidSecond, int jack, out int first, out int second)
    {
        var firsts = new int[TrainingGrid.COLUMNS * TrainingGrid.COLUMNS];
        var seconds = new int[firsts.Length];

        for (int relax = 0; relax < 2; relax++)
        {
            int count = 0;

            for (int a = 0; a < TrainingGrid.COLUMNS; a++)
            {
                if (a == jack || (relax == 0 && (avoidFirst & (1 << a)) != 0))
                    continue;

                for (int b = 0; b < TrainingGrid.COLUMNS; b++)
                {
                    if (b == jack || b == a)
                        continue;

                    if (relax == 0 && (avoidSecond & (1 << b)) != 0)
                        continue;

                    firsts[count] = a;
                    seconds[count] = b;
                    count++;
                }
            }

            if (count == 0)
                continue;

            int index = rng.Next(count);
            first = firsts[index];
            second = seconds[index];

            return;
        }

        // 도달 불가능. 최소한 그룹 안의 간격 규칙은 지킨다.
        first = (jack + 1) % TrainingGrid.COLUMNS;
        second = (jack + 2) % TrainingGrid.COLUMNS;
    }

    /// <summary>
    /// 컬럼당 정확히 <see cref="jacks_per_column"/>개이고 인접 중복이 없는 따닥 수열.
    /// 금지 전이가 없으므로 그 두 조건만 보면 된다.
    /// </summary>
    private static int[] buildJackSequence(Random rng, int forbiddenFirst)
    {
        var sequence = new int[groups_per_measure];

        for (int i = 0; i < sequence.Length; i++)
            sequence[i] = i / jacks_per_column;

        for (int attempt = 0; attempt < max_shuffle_attempts; attempt++)
        {
            for (int i = sequence.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (sequence[i], sequence[j]) = (sequence[j], sequence[i]);
            }

            if (isValid(sequence, forbiddenFirst))
                return sequence;
        }

        // 도달 불가능(무작위 배열의 통과 확률이 약 1/4). 조건을 항상 만족하는 순환 배열로 대체한다.
        int start = forbiddenFirst < 0 ? rng.Next(TrainingGrid.COLUMNS) : (forbiddenFirst + 1) % TrainingGrid.COLUMNS;

        for (int i = 0; i < sequence.Length; i++)
            sequence[i] = (start + i) % TrainingGrid.COLUMNS;

        return sequence;
    }

    private static bool isValid(int[] sequence, int forbiddenFirst)
    {
        if (sequence[0] == forbiddenFirst)
            return false;

        for (int i = 1; i < sequence.Length; i++)
        {
            if (sequence[i] == sequence[i - 1])
                return false;
        }

        return true;
    }
}
