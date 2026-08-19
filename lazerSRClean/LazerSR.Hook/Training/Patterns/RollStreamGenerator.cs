using System;
using System.Collections.Generic;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 드르륵. 매 슬롯 단노트 하나(마디당 32노트)를, <b>인접 컬럼으로 이어지는 짧은 런</b>을 이어 붙여 채운다.
/// <list type="number">
/// <item>시작 컬럼 → 방향(좌/우) → 길이 순으로 뽑아 런을 놓고, 끝나면 다음 슬롯에서 다시 뽑는다.</item>
/// <item>길이는 그 자리에서 <b>실제로 가능한 값 중에서만</b> 고른다(2~4). 방향이 곧장 벽이면 길이 1이 된다.</item>
/// <item>마디 끝에 걸리면 거기서 자른다.</item>
/// <item>간격 3 규칙은 필수. 컬럼 균등은 두지 않는다 — 가운데 컬럼이 자연히 더 자주 나온다.</item>
/// </list>
/// <para>
/// <b>런 내부는 검사할 필요가 없다.</b> 런의 컬럼은 연속된 정수라 전부 서로 다르고(최대 길이 4 = 컬럼 수),
/// 간격 3 규칙은 거리 2 이내만 문제 삼기 때문이다. 실제로 걸리는 곳은 런이 시작하는 지점뿐이며,
/// 직전 두 노트를 <c>P</c>(2슬롯 전) · <c>Q</c>(직전)라 할 때 조건은 둘로 압축된다.
/// </para>
/// <list type="bullet">
/// <item><c>S ∉ {P, Q}</c></item>
/// <item><c>S+d ≠ Q</c> (두 번째 노트가 존재할 때만)</item>
/// </list>
/// <para>
/// 세 번째 노트부터는 <c>Q</c>와 거리 3 이상이라 자동으로 안전하다. 또한 <c>{P, Q}</c>가 어떤 값이든
/// 위 조건을 만족하는 <c>(S, d)</c>가 항상 존재하므로 <b>막히는 경우가 없다</b> — 마디 재시도가 필요 없다.
/// </para>
/// <para>
/// <b>반복 억제</b>: 같은 런(<see cref="Run"/>)이 2연속으로 나오면 세 번째는 그 런을 쓰지 못한다.
/// 이 제약이 걸리는 이유는 병목 상태 때문이다 — <c>(P,Q)</c>가 <c>(1,3) (2,3) (3,2) (4,2)</c>일 때는
/// 길이 2 이상인 런의 시작점과 방향이 하나로 고정되고, 하필 <c>1,2,3</c>은 끝나면 <c>(2,3)</c>이 되어
/// 자기 자신을 다시 부른다. 배제해도 <b>어떤 상태에서든 후보가 최소 1개 남는다</b>(최악인 <c>(1,3)</c>과
/// <c>(4,2)</c>가 2개). 선택 분포를 건드리지 않으려고 배제는 재추첨으로 구현한다.
/// </para>
/// </summary>
public class RollStreamGenerator : IPatternGenerator
{
    /// <summary>런의 최대 길이. 4K에서는 컬럼 수와 같아 한 런이 전 컬럼을 훑는 것이 최대다.</summary>
    private const int max_run_length = 4;

    /// <summary>벽에 막히지 않은 상황에서 고를 수 있는 길이의 하한.</summary>
    private const int min_chosen_length = 2;

    private const int all_columns_mask = (1 << TrainingGrid.COLUMNS) - 1;

    /// <summary>재추첨 상한. 배제 대상이 뽑힐 확률은 어떤 상태에서도 1/2 이하라 넉넉하다.</summary>
    private const int max_reroll = 16;

    /// <summary>실제로 놓인 런 하나. 길이는 마디 끝 절단까지 반영된 값이다.</summary>
    private readonly record struct Run(int Start, int Direction, int Length);

    // 마디를 넘어서도 이어져야 하므로 인스턴스 필드로 들고 간다
    // (시퀀서가 생성기 인스턴스 하나를 세션 내내 재사용한다).
    private Run? lastRun;
    private Run? secondLastRun;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        TrainingMeasure.ReadTailMasks(previous, out int lastMask, out int secondLastMask);

        var measure = new TrainingMeasure();

        int slot = 0;

        while (slot < TrainingGrid.SLOTS_PER_MEASURE)
        {
            // 같은 런이 2연속으로 나왔다면 세 번째는 그것을 쓸 수 없다.
            Run? banned = lastRun is { } previousRun && secondLastRun == lastRun ? previousRun : null;

            Run run = pickRun(rng, lastMask, secondLastMask, TrainingGrid.SLOTS_PER_MEASURE - slot, banned);

            for (int i = 0; i < run.Length; i++)
            {
                int column = run.Start + run.Direction * i;

                measure[slot, column] = SlotState.Note;

                secondLastMask = lastMask;
                lastMask = 1 << column;
                slot++;
            }

            secondLastRun = lastRun;
            lastRun = run;
        }

        return measure;
    }

    /// <summary>
    /// 기존 추첨 절차(시작 → 방향 → 길이)를 그대로 쓰고, 배제 대상이 나오면 다시 뽑는다.
    /// 절차를 바꾸지 않으므로 <b>튜닝된 선택 분포가 그대로 유지</b>되고, 조건부 분포만 정확히 잘린다.
    /// </summary>
    private static Run pickRun(Random rng, int lastMask, int secondLastMask, int remainingSlots, Run? banned)
    {
        Run run = default;

        for (int attempt = 0; attempt < max_reroll; attempt++)
        {
            int start = pickStart(rng, lastMask, secondLastMask);
            int direction = pickDirection(rng, start, lastMask);

            // 마디 끝에 걸리면 자른다. 런이 마디를 넘어가지는 않는다.
            int length = Math.Min(pickLength(rng, start, direction), remainingSlots);

            run = new Run(start, direction, length);

            if (banned == null || run != banned.Value)
                return run;
        }

        // 도달할 일이 거의 없다. 배제보다 진행을 우선한다.
        return run;
    }

    /// <summary>직전 두 노트의 컬럼을 피해 시작 컬럼을 고른다.</summary>
    private static int pickStart(Random rng, int lastMask, int secondLastMask)
    {
        int candidates = all_columns_mask & ~lastMask & ~secondLastMask;

        // 동시치기가 많은 다른 패턴이 직전 마디로 들어와 후보가 없어지는 경우에 대한 방어.
        // 같은 패턴만 이어지는 정상 세션에서는 도달하지 않는다.
        if (candidates == 0)
            candidates = all_columns_mask & ~lastMask;

        if (candidates == 0)
            candidates = all_columns_mask;

        return pickFromMask(rng, candidates);
    }

    /// <summary>
    /// 방향을 고른다. 두 번째 노트가 <c>Q</c>와 겹치면 그 방향은 쓸 수 없다.
    /// 벽 바깥으로 향하는 방향은 길이 1짜리 런이 되며, 이는 허용된다.
    /// </summary>
    private static int pickDirection(Random rng, int start, int lastMask)
    {
        Span<int> valid = stackalloc int[2];
        int count = 0;

        for (int direction = -1; direction <= 1; direction += 2)
        {
            int next = start + direction;

            // 벽 바깥이면 두 번째 노트가 없으므로 조건이 성립할 것도 없다.
            bool ok = next < 0 || next >= TrainingGrid.COLUMNS
                ? true
                : (lastMask & (1 << next)) == 0;

            if (ok)
                valid[count++] = direction;
        }

        // 전수 확인 결과 최소 하나는 항상 남는다.
        return count == 0 ? 1 : valid[rng.Next(count)];
    }

    /// <summary>그 자리에서 실제로 놓을 수 있는 길이 중에서만 고른다.</summary>
    private static int pickLength(Random rng, int start, int direction)
    {
        int reachable = direction > 0
            ? TrainingGrid.COLUMNS - start
            : start + 1;

        int limit = Math.Min(reachable, max_run_length);

        // 방향이 곧장 벽이면 노트 하나만 놓인다.
        if (limit < min_chosen_length)
            return 1;

        return min_chosen_length + rng.Next(limit - min_chosen_length + 1);
    }

    private static int pickFromMask(Random rng, int mask)
    {
        var columns = new List<int>(TrainingGrid.COLUMNS);

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((mask & (1 << column)) != 0)
                columns.Add(column);
        }

        return columns[rng.Next(columns.Count)];
    }
}
