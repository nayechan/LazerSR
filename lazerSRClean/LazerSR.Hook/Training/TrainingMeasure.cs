namespace LazerSR.Hook.Training;

/// <summary>슬롯 하나의 상태. 홀드노트 확장 시 값만 추가하면 되도록 <c>bool</c>이 아닌 열거형이다.</summary>
public enum SlotState
{
    None = 0,
    Note,
}

/// <summary>
/// 모든 세부패턴이 공유하는 격자 절대규칙.
/// <para>
/// 1마디 = 32슬롯(32분음표) × 4컬럼 = 128슬롯. 슬롯 하나는 4/4 마디의 1/32이므로
/// 120bpm에서 62.5ms, 마디 전체는 2초가 된다. 16분음표는 2슬롯, 8분음표는 4슬롯 간격이다.
/// </para>
/// <para>
/// 이 변환은 <b>주입기만</b> 사용한다. 세부패턴 생성기는 bpm을 모르고 슬롯 위에서만 동작한다.
/// </para>
/// </summary>
public static class TrainingGrid
{
    public const int SLOTS_PER_MEASURE = 32;

    /// <summary>컬럼 수는 세션 도중 바꿀 수 없다 (<c>ManiaPlayfield</c>/<c>Stage</c> 생성자에서 고정).</summary>
    public const int COLUMNS = 4;

    public const int SLOTS_TOTAL = SLOTS_PER_MEASURE * COLUMNS;

    /// <summary>슬롯 하나의 길이(ms). 120bpm에서 62.5ms.</summary>
    public static double SlotMs(double bpm) => 7500.0 / bpm;

    /// <summary>마디 하나의 길이(ms). 120bpm에서 2000ms.</summary>
    public static double MeasureMs(double bpm) => SLOTS_PER_MEASURE * SlotMs(bpm);
}

/// <summary>
/// 세부패턴 생성기가 만들어내는 마디 하나. 시간 정보는 없고 슬롯 배치만 담는다.
/// <para>
/// 생성 도중 채워지므로 불변 record가 아니다 (<c>Data\</c>의 레코드 규칙 적용 대상이 아님).
/// 나중에 마디별 생성 파라미터(bpm·세부패턴·세트 id)를 기록하게 되면 여기에 필드로 붙는다.
/// </para>
/// </summary>
public class TrainingMeasure
{
    private readonly SlotState[] slots = new SlotState[TrainingGrid.SLOTS_TOTAL];

    public SlotState this[int slot, int column]
    {
        get => slots[slot * TrainingGrid.COLUMNS + column];
        set => slots[slot * TrainingGrid.COLUMNS + column] = value;
    }

    /// <summary>슬롯 하나에 놓인 컬럼들의 비트마스크. 동시치기면 비트가 여러 개 선다.</summary>
    public int ColumnMask(int slot)
    {
        int mask = 0;

        for (int column = 0; column < TrainingGrid.COLUMNS; column++)
        {
            if (this[slot, column] == SlotState.Note)
                mask |= 1 << column;
        }

        return mask;
    }

    /// <summary>
    /// 마디의 마지막 두 "노트가 있는 슬롯"을 컬럼 마스크로 꺼낸다.
    /// 컬럼 회피 규칙이 마디 경계를 넘어 적용되는 패턴들이 공유한다 — 동시치기가 섞인 패턴이
    /// 직전에 오더라도 그 슬롯의 컬럼 전부가 막히도록 마스크로 다룬다. 마디가 없으면 둘 다 0.
    /// </summary>
    public static void ReadTailMasks(TrainingMeasure? measure, out int lastMask, out int secondLastMask)
    {
        lastMask = 0;
        secondLastMask = 0;

        if (measure == null)
            return;

        for (int slot = TrainingGrid.SLOTS_PER_MEASURE - 1; slot >= 0; slot--)
        {
            int mask = measure.ColumnMask(slot);

            if (mask == 0)
                continue;

            if (lastMask == 0)
            {
                lastMask = mask;
                continue;
            }

            secondLastMask = mask;
            return;
        }
    }
}
