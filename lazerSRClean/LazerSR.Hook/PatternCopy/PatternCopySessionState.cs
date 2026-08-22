using osu.Framework.Bindables;

namespace LazerSR.Hook.PatternCopy;

/// <summary>
/// 패턴 복제 세션의 경계를 알리는 단일 슬롯.
/// <para>
/// newScreen에서 캡처가 시작돼 시간 원점이 잡히는 순간 <see cref="SessionIndex"/>가 올라간다.
/// 위젯은 이 값이 바뀌는 것만 보고 누적값을 리셋한다 — 무한 트레이닝에서 세트가 넘어갈 때
/// <c>TrainingSessionState.SetIndex</c>로 정확도를 리셋하는 것과 같은 방식이다.
/// </para>
/// <para>
/// 세션 경계 통지가 이것 하나뿐이라, 주입기는 원점을 잡을 때 이 값만 올리면 된다.
/// </para>
/// </summary>
public static class PatternCopySessionState
{
    /// <summary>캡처 세션이 바뀔 때마다 증가. 값 자체에는 의미가 없고 <b>변했다는 사실</b>만 쓴다.</summary>
    public static readonly Bindable<int> SessionIndex = new Bindable<int>();

    /// <summary>새 캡처 세션 시작. <b>업데이트 스레드에서만 부른다.</b></summary>
    public static void BeginSession() => SessionIndex.Value++;
}
