using System;
using System.Reflection;
using HarmonyLib;
using LazerSR.Hook.Screens;
using osu.Game.Screens.Play;

namespace LazerSR.Hook.Patches;

/// <summary>
/// 로컬 전용 세션(무한 트레이닝 / 구간 연습)의 로딩 화면에서 리더보드 서버 조회를 생략한다.
/// <para>
/// <c>PlayerLoader.LoadComplete</c>와 재시도 경로가 <c>refetchLeaderboard</c>를 호출해
/// <c>LeaderboardManager.FetchWithCriteria(...)</c>로 해당 비트맵의 리더보드를 서버에서 받아온다.
/// 읽기 요청이지만 "이 유저가 이 맵을 보고 있다"가 서버에 남으므로, 로컬 전용을 표방하는
/// 세션에서는 배제한다.
/// </para>
/// <para>
/// <b>Prefix를 쓰는 이유</b>: 원본 실행 자체를 막는 것이 목적이라 Postfix로는 대체할 수 없고,
/// <c>refetchLeaderboard</c>가 private이라 override도 불가능하다.
/// 이 패치는 리더보드 조회 요청을 생략시킬 뿐이며 점수/정확도/판정/리플레이 등
/// 서버 제출 경로의 어떤 값도 읽거나 쓰지 않는다.
/// </para>
/// </summary>
[HarmonyPatch]
public static class LocalOnlyLeaderboardSkipPatch
{
    public static MethodBase? TargetMethod() => AccessTools.Method(typeof(PlayerLoader), "refetchLeaderboard");

    public static bool Prepare() => TargetMethod() != null;

    /// <returns>false면 원본을 실행하지 않는다 — 우리 로더일 때만 생략.</returns>
    public static bool Prefix(object __instance)
    {
        try
        {
            return __instance is not ILocalOnlyPlayerLoader;
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] LocalOnlyLeaderboardSkipPatch.Prefix failed: {ex}");
            return true;
        }
    }
}
