using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.UI;

namespace LazerSR.Hook.PatternCopy;

/// <summary>
/// newScreen이 보내온 노트를 실제 <see cref="Playfield"/>에 주입한다.
/// <para>
/// 노트 생성 자체는 무한 트레이닝의 주입부와 같은 세 줄(<c>new</c> → <c>ApplyDefaults</c> → <c>Add</c>)이다.
/// 다른 점은 <b>패턴이 실시간으로 들어온다</b>는 것과, 끝을 모르는 롱노트를 나중에 잘라야 한다는 것이다.
/// </para>
/// </summary>
internal sealed class PatternCopyInjector
{
    /// <summary>
    /// 끝을 모르는 롱노트를 일단 이만큼 길게 깔아둔다. 실제 곡의 어떤 롱노트보다 길어야 한다.
    /// </summary>
    private const double open_hold_duration_ms = 100_000;

    private readonly Playfield playfield;
    private readonly IBeatmap beatmap;
    private readonly IBeatmapDifficultyInfo difficulty;

    /// <summary>아직 길이가 확정되지 않은 롱노트들. newScreen이 준 id로 찾는다.</summary>
    private readonly Dictionary<int, HoldNote> openHolds = new();

    /// <summary>
    /// newScreen의 시각 0에 해당하는 osu! 게임플레이 시각. 두 시계의 원점을 잇는 값이다.
    /// null이면 아직 <c>pc:start</c>를 못 받은 것이라 노트를 놓을 자리를 모른다.
    /// </summary>
    private double? originMs;

    public PatternCopyInjector(Playfield playfield, IBeatmap beatmap)
    {
        this.playfield = playfield;
        this.beatmap = beatmap;
        difficulty = beatmap.Difficulty;
    }

    public void Process(in PatternCommand command, double currentTimeMs)
    {
        switch (command.Kind)
        {
            case PatternCommandKind.Sync:
                // 보낸 쪽의 "지금"이 우리 쪽의 "지금"이다. 그 차이가 두 시계의 오프셋이 된다.
                // 매 틱 오지만 <b>원점이 없을 때만</b> 잡는다 — 매번 갱신하면 원점이 흔들린다.
                if (originMs == null)
                {
                    originMs = currentTimeMs - command.TimeMs;
                    openHolds.Clear();

                    // 여기가 곧 "캡처가 시작됐다" — 위젯이 누적값을 리셋하는 지점이다.
                    PatternCopySessionState.BeginSession();
                }

                break;

            case PatternCommandKind.Note:
                if (command.DurationMs > 0)
                    addHold(command.Id, command.Column, ToGameplayTime(command.TimeMs), command.DurationMs, keepOpen: false, currentTimeMs);
                else
                    addTap(command.Column, ToGameplayTime(command.TimeMs), currentTimeMs);
                break;

            case PatternCommandKind.OpenHold:
                addHold(command.Id, command.Column, ToGameplayTime(command.TimeMs), open_hold_duration_ms, keepOpen: true, currentTimeMs);
                break;

            case PatternCommandKind.Cut:
                cut(command.Id, command.DurationMs, currentTimeMs);
                break;

            case PatternCommandKind.Stop:
                // 열린 롱노트를 그대로 두면 100초짜리가 화면에 남는다. 지금 시점에서 끊는다.
                foreach (var note in openHolds.Values)
                    HoldNoteTruncator.Truncate(playfield, note, currentTimeMs - note.StartTime, currentTimeMs);

                openHolds.Clear();
                originMs = null;
                break;
        }
    }

    private double ToGameplayTime(double senderMs) => (originMs ?? 0) + senderMs;

    private void addTap(int column, double startTime, double currentTimeMs)
    {
        if (!canPlace(column, startTime, currentTimeMs)) return;

        var note = new Note { StartTime = startTime, Column = column };
        note.ApplyDefaults(beatmap.ControlPointInfo, difficulty);
        playfield.Add(note);
    }

    private void addHold(int id, int column, double startTime, double duration, bool keepOpen, double currentTimeMs)
    {
        if (!canPlace(column, startTime, currentTimeMs)) return;

        var note = new HoldNote { StartTime = startTime, Column = column, Duration = duration };
        note.ApplyDefaults(beatmap.ControlPointInfo, difficulty);
        playfield.Add(note);

        if (keepOpen)
            openHolds[id] = note;
    }

    private void cut(int id, double duration, double currentTimeMs)
    {
        if (!openHolds.Remove(id, out var note)) return;

        HoldNoteTruncator.Truncate(playfield, note, duration, currentTimeMs);
    }

    /// <summary>
    /// 노트를 놓을 수 있는지. 시간 원점이 아직 없거나, 컬럼이 범위 밖이거나,
    /// <b>이미 지나간 시각</b>이면 놓지 않는다 — 과거에 놓으면 <c>OrderedHitPolicy</c>가 즉시
    /// 강제 미스로 처리하므로, 리드타임이 모자라 늦게 도착한 노트는 버리는 편이 낫다.
    /// </summary>
    private bool canPlace(int column, double startTime, double currentTimeMs)
    {
        if (originMs == null) return false;
        if (column < 0 || column >= Screens.PatternCopyBeatmap.KEY_COUNT) return false;

        return startTime > currentTimeMs;
    }
}
