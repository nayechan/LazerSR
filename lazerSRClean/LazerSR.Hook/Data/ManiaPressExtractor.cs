using System;
using System.Collections.Generic;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Replays;

namespace LazerSR.Hook.Data;

/// <summary>리플레이에 기록된 키 입력 한 번.</summary>
public readonly record struct ManiaPress(double Start, double End);

/// <summary>mania 리플레이 프레임을 컬럼별 "누른 구간" 목록으로 바꾼다.</summary>
public static class ManiaPressExtractor
{
    /// <summary>
    /// mania 리플레이 프레임은 이벤트가 아니라 <b>그 시점에 눌려 있는 키 집합의 스냅샷</b>이다.
    /// 따라서 연속한 두 프레임의 <see cref="ManiaReplayFrame.Actions"/>를 비교해 누름/뗌을 복원한다.
    /// </summary>
    public static List<ManiaPress>[] Extract(Replay replay, int totalColumns)
    {
        var result = new List<ManiaPress>[totalColumns];
        for (int c = 0; c < totalColumns; c++)
            result[c] = new List<ManiaPress>();

        var openSince = new double[totalColumns];
        var isOpen = new bool[totalColumns];
        var held = new bool[totalColumns];

        double lastTime = 0;

        foreach (var frame in replay.Frames)
        {
            if (frame is not ManiaReplayFrame maniaFrame)
                continue;

            Array.Clear(held, 0, totalColumns);

            foreach (var action in maniaFrame.Actions)
            {
                int column = action - ManiaAction.Key1;

                if (column >= 0 && column < totalColumns)
                    held[column] = true;
            }

            for (int c = 0; c < totalColumns; c++)
            {
                if (held[c] && !isOpen[c])
                {
                    isOpen[c] = true;
                    openSince[c] = maniaFrame.Time;
                }
                else if (!held[c] && isOpen[c])
                {
                    isOpen[c] = false;
                    result[c].Add(new ManiaPress(openSince[c], maniaFrame.Time));
                }
            }

            lastTime = maniaFrame.Time;
        }

        // 리플레이가 끝날 때까지 떼지 않은 키는 마지막 프레임에서 끊는다.
        for (int c = 0; c < totalColumns; c++)
        {
            if (isOpen[c])
                result[c].Add(new ManiaPress(openSince[c], Math.Max(lastTime, openSince[c])));
        }

        return result;
    }
}
