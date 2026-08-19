using System;
using System.Reflection;
using HarmonyLib;
using LazerSR.Hook.Data;
using osu.Game.Screens.Play;

namespace LazerSR.Hook.Patches;

/// <summary>
/// 판정 시뮬레이션이 끝날 때까지 로딩 화면을 붙잡아 둔다.
///
/// <para>
/// 단순한 편의 기능이 아니라 <b>구조상 필수</b>다. 시뮬레이션 드로어블은 <c>PlayerLoader</c>에 붙어 있고,
/// 로더가 서스펜드되면(= 게임플레이 화면으로 넘어가면) 업데이트가 끊겨 시뮬레이션이 그 자리에서 멈춘다
/// (2026-07-29 로그로 확인). 따라서 완주시키려면 로더가 현재 화면인 동안 끝내야 한다.
/// </para>
///
/// <para>
/// <c>PlayerLoader.ReadyForGameplay</c>는 osu!가 이미 갖고 있는 게이트다 — false인 동안 플레이어를
/// push하지 않고, 대기 표시(<c>MetadataInfo.UserBlocked</c>)까지 알아서 해준다. 여기에 조건 하나를 더한다.
/// </para>
///
/// <para>
/// <b>진행 중이면 끝까지 기다린다.</b> 총 소요 시간으로 자르면 정상 동작을 잘라먹기 때문이다
/// (2026-07-29에 13.5분 맵이 95.4%에서 잘렸다). 대신 <see cref="ManiaSimulationResult.Alive"/>가
/// "10초간 진척 없음"을 감지해 시뮬레이션이 죽은 경우에만 게이트를 푼다. 그 경우 사각형은
/// 회색으로 나오고 숫자가 없을 뿐, 리플레이 감상은 막히지 않는다.
/// </para>
/// </summary>
[HarmonyPatch]
public static class ManiaSimulationGatePatch
{
    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName("osu.Game.Screens.Play.PlayerLoader");
        return type == null ? null : AccessTools.PropertyGetter(type, "ReadyForGameplay");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance, ref bool __result)
    {
        try
        {
            // 이미 osu! 자체 조건으로 대기 중이면 우리가 더 할 일이 없다.
            if (!__result)
                return;

            if (__instance is not PlayerLoader loader)
                return;

            var simulation = ManiaSimulationState.Current;

            if (simulation == null || !ReferenceEquals(simulation.Score, loader.CurrentPlayer?.Score))
                return;

            if (!simulation.ShouldBlockLoading)
                return;

            __result = false;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] ManiaSimulationGatePatch.Postfix failed: {e}");
        }
    }
}
