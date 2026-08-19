using osu.Framework.Bindables;

namespace LazerSR.Hook.Data;

/// <summary>
/// 리플레이 판정 표시 4종의 켜기/끄기 상태.
///
/// <para>
/// 리플레이 설정 창(<c>ManiaReplayDisplaySettings</c>)이 쓰고 오버레이들이 읽는다.
/// 전부 기본값 꺼짐이다 — 필요할 때만 켜서 보는 도구이므로.
/// 프로세스가 살아있는 동안만 유지되며 config에 저장하지 않는다 — 리플레이마다 다시 켜는 게 아니라
/// 세션 안에서 한 번 정하면 계속 간다는 뜻이다.
/// </para>
///
/// <para>
/// 구독하는 쪽은 반드시 <c>GetBoundCopy()</c>나 자체 <c>BindableBool</c> 필드를 거쳐야 한다.
/// static bindable의 이벤트에 델리게이트가 직접 남으면 오버레이가 GC되지 않는다.
/// </para>
/// </summary>
public static class ManiaOverlayVisibility
{
    /// <summary>노트별 판정 기준선 (흰 가로선 + 롱노트 세로 연결선).</summary>
    public static readonly BindableBool NoteLines = new BindableBool();

    /// <summary>판정이 실제로 일어나는 위치의 고정 흰 선.</summary>
    public static readonly BindableBool JudgementLine = new BindableBool();

    /// <summary>리플레이 입력 구간 사각형 (노랑 = 판정 발생, 회색 = 미발생).</summary>
    public static readonly BindableBool PressBoxes = new BindableBool();

    /// <summary>판정 오차 숫자 (파랑 = 얼리, 빨강 = 레이트).</summary>
    public static readonly BindableBool OffsetNumbers = new BindableBool();

    /// <summary>노트를 감춘다 (우리가 그린 표시만 남기고 보기 위함). 기본값은 끔.</summary>
    public static readonly BindableBool HideNotes = new BindableBool();
}
