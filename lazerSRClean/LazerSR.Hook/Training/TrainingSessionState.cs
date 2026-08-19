using System;
using osu.Framework.Bindables;

namespace LazerSR.Hook.Training;

/// <summary>세션의 큰 단계. 상태 제목 컨테이너에 그대로 표시된다.</summary>
public enum TrainingPhase
{
    ShortTerm,
    Infinite,
}

/// <summary>다목적 컨테이너의 표시 모드.</summary>
public enum TrainingMode
{
    /// <summary>측정/무한세션 진행 중. 세트 사이의 짧은 휴식도 여기에 포함된다.</summary>
    Playing,

    /// <summary>세부패턴 하나가 끝나고 다음으로 넘어가기 전. 사용자 응답을 기다린다.</summary>
    Resting,
}

/// <summary>다목적 컨테이너의 한 행. bpm/SR은 값이 없으면 <c>null</c> 슬롯으로 둔다.</summary>
public record TrainingSlot(string DisplayName, double Bpm, double StarRating);

/// <summary>
/// 무한 트레이닝 세션의 단일 상태 슬롯. **측정 알고리즘이 쓰고 위젯이 읽는다.**
/// <para>
/// 아직 알고리즘이 없으므로 지금은 아무도 쓰지 않는다 — <see cref="Active"/>가 false인 동안
/// 위젯은 스킨 에디터 배치용 플레이스홀더를 보여준다.
/// </para>
/// <para>
/// 위젯이 이 값들을 구독할 때는 <c>GetBoundCopy()</c>를 거친다. static 필드에 직접
/// <c>BindValueChanged</c>를 걸면 델리게이트가 강한 참조로 남아 위젯이 GC되지 않는다.
/// </para>
/// </summary>
public static class TrainingSessionState
{
    /// <summary>세션이 실제로 돌고 있는가. false면 위젯은 플레이스홀더를 그린다.</summary>
    public static readonly BindableBool Active = new BindableBool();

    public static readonly Bindable<TrainingPhase> Phase = new Bindable<TrainingPhase>(TrainingPhase.ShortTerm);

    public static readonly Bindable<TrainingMode> Mode = new Bindable<TrainingMode>(TrainingMode.Playing);

    /// <summary>직전 세부패턴. 측정이 끝났으므로 <b>최종 기록된 bpm</b>이 들어간다.</summary>
    public static readonly Bindable<TrainingSlot?> Previous = new Bindable<TrainingSlot?>();

    /// <summary>진행 중인 세부패턴. bpm/SR이 세트마다 갱신된다.</summary>
    public static readonly Bindable<TrainingSlot?> Current = new Bindable<TrainingSlot?>();

    /// <summary>다음 세부패턴. 아직 시작 전이므로 <b>초기 bpm</b>이 들어간다.</summary>
    public static readonly Bindable<TrainingSlot?> Next = new Bindable<TrainingSlot?>();

    /// <summary>다다음 세부패턴. 없으면 null(공백).</summary>
    public static readonly Bindable<TrainingSlot?> AfterNext = new Bindable<TrainingSlot?>();

    /// <summary>휴식 모드에서 보여줄 확정 결과.</summary>
    public static readonly Bindable<TrainingSlot?> RestingResult = new Bindable<TrainingSlot?>();

    /// <summary>진행 중인 세트 번호. <see cref="JudgementAggregator"/>가 갱신한다.</summary>
    public static readonly BindableInt SetIndex = new BindableInt();

    /// <summary>
    /// 진행 중인 세트의 정확도(0~1). <see cref="JudgementAggregator"/>가 유일한 계산 주체이고
    /// 위젯은 이 값을 표시만 한다 — 화면의 숫자와 알고리즘의 판단 근거가 갈라지지 않는다.
    /// 집계된 노트가 없으면 1.0이다.
    /// </summary>
    public static readonly BindableDouble CurrentAccuracy = new BindableDouble(1);

    /// <summary>
    /// 휴식 모드 버튼 → 측정 알고리즘. <c>true</c>면 저장, <c>false</c>면 폐기.
    /// 알고리즘이 여기 구독해서 다음 세부패턴으로 넘어가는 신호로 쓴다.
    /// </summary>
    public static event Action<bool>? RestDecisionSubmitted;

    public static void SubmitRestDecision(bool save) => RestDecisionSubmitted?.Invoke(save);

    /// <summary>세션 시작/종료 시 호출. static 상태라 이전 세션 값이 남지 않도록 명시적으로 비운다.</summary>
    public static void Reset()
    {
        Active.Value = false;
        Phase.Value = TrainingPhase.ShortTerm;
        Mode.Value = TrainingMode.Playing;
        Previous.Value = null;
        Current.Value = null;
        Next.Value = null;
        AfterNext.Value = null;
        RestingResult.Value = null;
        SetIndex.Value = 0;
        CurrentAccuracy.Value = 1;
    }
}
