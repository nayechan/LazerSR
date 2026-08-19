using System;

namespace LazerSR.Hook.Training.Patterns;

/// <summary>
/// 축연타. 한 컬럼을 마디 내내 16번 치고(축), 4이벤트마다 동치가 얹힌다.
/// 2슬롯마다 이벤트가 오므로 <b>축 1블록 = 정확히 1마디</b>이며, 마디당 22노트다.
/// </summary>
/// <remarks>
/// <code>
/// 이벤트  0  1  2  3   4  5  6  7   8  9 10 11  12 13 14 15
///        ②  a  a  a   ③  a  a  a   ②  a  a  a   ③  a  a  a
/// </code>
/// <para>
/// 모든 이벤트가 축 <c>a</c>를 포함한다. <b>2동치 = {a, 반대손 1개}</b>,
/// <b>3동치 = {a, 파트너(a), 반대손 1개}</b> — 축과 같은 손에는 컬럼이 하나뿐이라 파트너는 강제된다.
/// 자유 변수는 동치 4개의 반대손 컬럼뿐이다.
/// </para>
/// <para>
/// <b>축은 손을 교대하며 봉지에서 뽑는다.</b> 네 컬럼을 <c>왼–오–왼–오</c> 순으로 배열해 하나씩 쓰므로
/// 세트(2마디)마다 축이 서로 다른 손에 하나씩 오고, <b>4마디마다 네 컬럼이 한 번씩</b> 축이 된다.
/// 이 조합이 노트 균등에 필요한 최소 창이다 — 2마디만으로는 축(16노트)과 파트너(2노트)의 격차를
/// 어떤 배분으로도 메울 수 없다. 4마디 창에서 컬럼당 <c>16 + 2 + 4 = 22</c>노트로 정확히 맞으며,
/// 그러려면 <b>동치 4개의 반대손 컬럼을 2–2로</b> 나누기만 하면 된다.
/// </para>
/// <para>
/// <b>축 선택 제약</b>: 직전 마디 꼬리에서 이미 2연타가 걸린 컬럼(마지막 두 이벤트에 모두 있는 컬럼)은
/// 축이 될 수 없다. 우리 패턴끼리 이어질 때는 그 컬럼이 직전 축 하나뿐이고 손 교대가 이미 막고 있어
/// 자동으로 성립하며, 실제로 일하는 건 <b>다른 패턴에서 넘어올 때</b>다. 우리가 만든 어떤 패턴도
/// 4동치로 두 번 연속 끝나지 않으므로 후보가 항상 남는다.
/// </para>
/// <para>
/// 탐색 요소가 없어 데드락·재시도가 아예 없다. 마디 변형은 <c>C(4,2) = 6</c>가지로 가장 낮은데,
/// "한 컬럼 16연타"가 자유도를 다 먹기 때문이라 구조적으로 불가피하다. 체감 반복 주기는 4마디다.
/// </para>
/// </remarks>
public class AxisJackGenerator : IPatternGenerator
{
    /// <summary>이벤트 간격(슬롯).</summary>
    private const int event_interval_slots = 2;

    private const int events_per_measure = TrainingGrid.SLOTS_PER_MEASURE / event_interval_slots;

    /// <summary>동치가 얹히는 주기(이벤트).</summary>
    private const int chord_interval_events = 4;

    private const int chords_per_measure = events_per_measure / chord_interval_events;

    private readonly int[] bag = new int[TrainingGrid.COLUMNS];

    private int bagIndex = TrainingGrid.COLUMNS;
    private int lastAxis = -1;

    public TrainingMeasure Generate(Random rng, TrainingMeasure? previous)
    {
        TrainingMeasure.ReadTailMasks(previous, out int lastMask, out int secondLastMask);

        // 직전 마디를 우리가 만들었으면 마지막 두 이벤트가 둘 다 직전 축 단노트다.
        bool continuous = lastAxis >= 0 && lastMask == 1 << lastAxis && secondLastMask == lastMask;

        if (!continuous)
        {
            bagIndex = bag.Length;
            lastAxis = -1;
        }

        // 꼬리에서 이미 2연타가 걸린 컬럼은 축이 될 수 없다.
        int axis = drawAxis(rng, lastMask & secondLastMask);
        int partner = axis ^ 1;
        int[] opposites = buildOpposites(rng, axis);

        var measure = new TrainingMeasure();

        for (int i = 0; i < events_per_measure; i++)
        {
            int slot = i * event_interval_slots;

            measure[slot, axis] = SlotState.Note;

            if (i % chord_interval_events != 0)
                continue;

            int chordIndex = i / chord_interval_events;

            measure[slot, opposites[chordIndex]] = SlotState.Note;

            // 짝수 번째는 2동치, 홀수 번째는 3동치 — 3동치만 같은 손 파트너가 붙는다.
            if (chordIndex % 2 == 1)
                measure[slot, partner] = SlotState.Note;
        }

        return measure;
    }

    /// <summary>동치 4개에 붙일 반대손 컬럼. 두 컬럼을 2번씩 써야 4마디 창의 노트 균등이 맞는다.</summary>
    private static int[] buildOpposites(Random rng, int axis)
    {
        int otherHand = 1 - axis / 2;
        int first = otherHand * 2;

        var opposites = new int[chords_per_measure];

        for (int i = 0; i < opposites.Length; i++)
            opposites[i] = i < opposites.Length / 2 ? first : first + 1;

        for (int i = opposites.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (opposites[i], opposites[j]) = (opposites[j], opposites[i]);
        }

        return opposites;
    }

    private int drawAxis(Random rng, int blockedMask)
    {
        if (bagIndex >= bag.Length)
            refill(rng, blockedMask);

        lastAxis = bag[bagIndex++];

        return lastAxis;
    }

    /// <summary>
    /// 네 컬럼을 손이 교대하도록 배열한다. 첫 컬럼만 정하면 나머지는 따라온다 —
    /// 이어지는 중이면 직전 축의 반대손에서, 아니면 막히지 않은 컬럼 중에서 고른다.
    /// </summary>
    private void refill(Random rng, int blockedMask)
    {
        var candidates = new int[TrainingGrid.COLUMNS];
        int count = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if ((blockedMask & (1 << column)) != 0)
                continue;

            if (lastAxis >= 0 && column / 2 == lastAxis / 2)
                continue;

            candidates[count++] = column;
        }

        // 4동치가 두 번 연속으로 끝나는 패턴이 없으므로 후보가 비는 일은 없다.
        int first = count == 0 ? rng.Next(TrainingGrid.COLUMNS) : candidates[rng.Next(count)];
        int otherHand = 1 - first / 2;
        int otherFirst = otherHand * 2 + rng.Next(2);

        bag[0] = first;
        bag[1] = otherFirst;
        bag[2] = first ^ 1;
        bag[3] = otherFirst ^ 1;

        bagIndex = 0;
    }
}
