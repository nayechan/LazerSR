using System;
using System.Reflection;
using HarmonyLib;
using LazerSR.Hook.Data;
using LazerSR.Hook.Drawables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Screens.Play;

namespace LazerSR.Hook.Patches;

/// <summary>
/// 로딩 화면에서 리플레이 판정 시뮬레이션을 미리 돌린다.
///
/// <para>
/// <c>PlayerLoader.OnPlayerLoaded</c>는 <c>Player</c>가 로드를 마친 직후이면서 아직 게임플레이 화면으로
/// 넘어가기 전이다 (<c>PlayerPushDelay</c>가 1.8초 이상). 시뮬레이션에서 생기는 끊김이 로딩 화면에
/// 묻히고, 첫 노트가 판정되기 전에 결과가 준비된다.
/// </para>
///
/// <para>읽기 전용 — 본 게임플레이 상태에는 아무것도 쓰지 않는다.</para>
/// </summary>
[HarmonyPatch]
public static class ManiaReplaySimulationPatch
{
    private static readonly MethodInfo? add_internal =
        AccessTools.Method(typeof(CompositeDrawable), "AddInternal", new[] { typeof(Drawable) });

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName("osu.Game.Screens.Play.PlayerLoader");
        return type == null ? null : AccessTools.Method(type, "OnPlayerLoaded");
    }

    public static bool Prepare() => TargetMethod() != null && add_internal != null;

    public static void Postfix(object __instance)
    {
        try
        {
            if (__instance is not PlayerLoader loader)
                return;

            // 관전은 리플레이 프레임이 실시간 스트리밍이라 미리 돌릴 대상이 없다.
            if (loader.CurrentPlayer is not ReplayPlayer player)
                return;

            var gameplayState = player.GameplayState;

            if (gameplayState.Beatmap is not ManiaBeatmap)
                return;

            var score = player.Score;

            if (score?.Replay == null || score.Replay.Frames.Count == 0)
                return;

            // 같은 스코어에 대해 이미 돌렸으면 재사용한다 (재시작 시 중복 방지).
            if (ManiaSimulationState.Current is { } existing && ReferenceEquals(existing.Score, score))
                return;

            var result = new ManiaSimulationResult(score);

            // 부착에 성공한 뒤에만 슬롯에 올린다 — 실패한 결과를 올려두면
            // 게이트(ManiaSimulationGatePatch)가 아무도 채우지 않을 값을 기다리며 로딩을 붙잡는다.
            add_internal!.Invoke(loader, new object[]
            {
                // 변환보면(osu! → mania 등)을 위해 룰셋은 비트맵이 아니라 게임플레이 상태에서 가져온다.
                new ManiaJudgementSimulation(score, gameplayState.Ruleset.RulesetInfo, gameplayState.Mods, result)
            });

            add_internal.Invoke(loader, new object[] { new ManiaSimulationProgressDisplay(result) });

            ManiaSimulationState.Current = result;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] ManiaReplaySimulationPatch.Postfix failed: {e}");
        }
    }
}
