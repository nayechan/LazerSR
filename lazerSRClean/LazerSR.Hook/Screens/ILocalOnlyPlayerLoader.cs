namespace LazerSR.Hook.Screens;

/// <summary>
/// 서버와 접촉하지 않는 로컬 전용 세션의 <c>PlayerLoader</c>임을 나타내는 마커.
/// <see cref="LazerSR.Hook.Patches.LocalOnlyLeaderboardSkipPatch"/>가 이 타입일 때만
/// 리더보드 서버 조회를 생략한다 — 일반 플레이 경로에는 영향이 없다.
/// </summary>
public interface ILocalOnlyPlayerLoader
{
}
