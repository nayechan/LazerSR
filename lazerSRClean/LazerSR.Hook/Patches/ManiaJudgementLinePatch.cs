using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LazerSR.Hook.Data;
using LazerSR.Hook.Drawables;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace LazerSR.Hook.Patches;

/// <summary>
/// 리플레이 재생/관전 중 mania 플레이필드에 "실제 판정 기준선"을 흰 가로선으로 표시한다.
///
/// 읽기 전용 — 비트맵의 노트 시각만 읽어 새 Drawable을 얹는다.
/// 판정 경로나 라이브 <c>ScoreProcessor</c>는 건드리지 않는다.
/// </summary>
[HarmonyPatch]
public static class ManiaJudgementLinePatch
{
    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName("osu.Game.Screens.Play.Player");
        return type == null ? null : AccessTools.Method(type, "LoadComplete");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            // 직접 플레이 중에는 켜지 않는다. 리플레이 재생과 관전만 대상.
            if (__instance is not ReplayPlayer && __instance is not SpectatorPlayer)
                return;

            if (!AccessHelper.TryGet<DrawableRuleset>(typeof(Player), "DrawableRuleset", __instance, out var drawableRuleset)
                || drawableRuleset == null)
                return;

            if (drawableRuleset.Playfield is not ManiaPlayfield maniaPlayfield)
                return;

            int totalColumns = maniaPlayfield.TotalColumns;
            var data = collect(drawableRuleset.Objects, totalColumns);

            // 리플레이 프레임은 관전에서 실시간 스트리밍이라 이 시점엔 비어 있다 → 관전에서는 판정선만 나온다.
            var score = (__instance as Player)?.Score;
            var presses = score?.Replay == null
                ? null
                : ManiaPressExtractor.Extract(score.Replay, totalColumns);

            // 로딩 화면에서 돌린 시뮬레이션 결과. 아직 안 끝났을 수 있으므로 오버레이가 준비되면 집어간다.
            var simulation = ManiaSimulationState.Current is { } current && ReferenceEquals(current.Score, score)
                ? current
                : null;


            for (int i = 0; i < totalColumns; i++)
            {
                var column = maniaPlayfield.GetColumn(i);

                double[] times = data[i].Times.ToArray();
                Array.Sort(times);

                // 릴리즈 시각 기준으로 정렬해야 오버레이가 "아직 끝나지 않은 롱노트"를 이분 탐색으로 찾을 수 있다.
                data[i].Holds.Sort((a, b) => a.End.CompareTo(b.End));

                double[] holdStarts = data[i].Holds.Select(h => h.Start).ToArray();
                double[] holdEnds = data[i].Holds.Select(h => h.End).ToArray();

                // 입력 사각형은 노트 뒤(UnderlayElements), 판정선과 오차 숫자는 노트 앞(HitObjectArea).
                // 사각형이 노트를 가리지 않으면서 흰 선과 숫자는 항상 읽히게 하는 배치.
                if (presses != null && presses[i].Count > 0)
                {
                    var columnPresses = presses[i];
                    columnPresses.Sort((a, b) => a.End.CompareTo(b.End));

                    var pressOverlay = new ManiaPressOverlay(
                        column.HitObjectContainer,
                        i,
                        columnPresses.Select(p => p.Start).ToArray(),
                        columnPresses.Select(p => p.End).ToArray(),
                        simulation);

                    column.UnderlayElements.Add(pressOverlay);
                    column.HitObjectArea.Add(pressOverlay.TextLayer);
                }

                column.HitObjectArea.Add(new ManiaJudgementLineOverlay(column.HitObjectContainer, times, holdStarts, holdEnds));
                column.UnderlayElements.Add(new ManiaNoteHider(column.HitObjectContainer));
            }

            // 리플레이 설정 창(오른쪽 패널)에 표시 토글 그룹을 추가한다. 관전에는 그 패널이 없다.
            if (__instance is ReplayPlayer replayPlayer)
                replayPlayer.AddSettings(new ManiaReplayDisplaySettings());

        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] ManiaJudgementLinePatch.Postfix failed: {e}");
        }
    }

    private readonly record struct HoldSpan(double Start, double End);

    private sealed class ColumnData
    {
        /// <summary>단노트 + 롱노트 헤드의 판정 시각 (컬럼 폭 전체 가로선).</summary>
        public readonly List<double> Times = new List<double>();

        /// <summary>롱노트 (릴리즈 가로선 + 시작~끝 세로선).</summary>
        public readonly List<HoldSpan> Holds = new List<HoldSpan>();
    }

    /// <summary>
    /// 단노트는 <c>StartTime</c> 하나, 롱노트는 <c>StartTime</c>(헤드)과 <c>EndTime</c>(릴리즈) 둘.
    /// 릴리즈 판정창은 1.5배 관대하지만(<c>TailNote.RELEASE_WINDOW_LENIENCE</c>) 중심은 정확히 <c>EndTime</c>이다.
    /// </summary>
    private static ColumnData[] collect(IEnumerable<HitObject> objects, int totalColumns)
    {
        var data = new ColumnData[totalColumns];
        for (int i = 0; i < totalColumns; i++)
            data[i] = new ColumnData();

        foreach (var obj in objects)
        {
            if (obj is not ManiaHitObject maniaObject)
                continue;

            int column = maniaObject.Column;
            if (column < 0 || column >= totalColumns)
                continue;

            data[column].Times.Add(maniaObject.StartTime);

            if (maniaObject is HoldNote hold)
                data[column].Holds.Add(new HoldSpan(hold.StartTime, hold.EndTime));
        }

        return data;
    }
}
