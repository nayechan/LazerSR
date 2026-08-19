using osu.Framework.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Tests.Beatmaps;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 무한 트레이닝의 시드 비트맵. 파일에서 읽지 않고 코드로 만든다.
/// <para>
/// 노트는 총 8개 — <see cref="FIRST_CHORD_TIME_MS"/>에 전 칼럼 동시치기 1회,
/// <see cref="SESSION_LENGTH_MS"/>에 한 번 더. 뒤쪽 동시치기는 <b>맵 길이를 확보하기 위한 것</b>으로,
/// 실제로 거기까지 플레이하는 경우는 상정하지 않는다.
/// </para>
/// <para>
/// 시작 시점에 노트가 최소 1개는 있어야 한다 — <c>Player.LoadedBeatmapSuccessfully</c>가
/// <c>DrawableRuleset.Objects.Any()</c>로 판정하고, false면 <c>PlayerLoader</c>가 즉시 화면을 빠져나간다.
/// </para>
/// </summary>
internal static class InfiniteTrainingBeatmap
{
    /// <summary>키 수. 컬럼 수는 세션 도중 바꿀 수 없으므로 여기서만 결정된다 (7K 확장 시 이 값을 인자로 뺀다).</summary>
    public const int KEY_COUNT = 4;

    /// <summary>첫 동시치기 시각.</summary>
    public const double FIRST_CHORD_TIME_MS = 1000;

    /// <summary>맵 길이 = 마지막 동시치기 시각. 무음 가상 트랙 길이도 여기서 파생된다 (약 83분).</summary>
    public const double SESSION_LENGTH_MS = 5_000_000;

    /// <param name="overallDifficulty">
    /// 사용자가 설정한 OD. 주입되는 노트가 <c>ApplyDefaults</c>에서 이 값으로 판정창을 잡는다
    /// (<c>HitObject.ApplyDefaultsToSelf</c> → <c>HitWindows.SetDifficulty</c>).
    /// 판정창은 비트맵이 아니라 <c>ApplyDefaults</c>에 넘기는 난이도 객체가 결정하므로,
    /// 나중에 측정 단계 노트만 OD 8.5로 따로 주입하는 것도 가능하다.
    /// </param>
    public static WorkingBeatmap Create(RulesetInfo maniaRuleset, AudioManager audio, double overallDifficulty)
    {
        var beatmapInfo = new BeatmapInfo
        {
            Ruleset = maniaRuleset,
            // 컨버터가 CircleSize를 반올림해 컬럼 수로 쓴다 (ManiaBeatmapConverter: IsForCurrentRuleset 경로).
            Difficulty = new BeatmapDifficulty
            {
                CircleSize = KEY_COUNT,
                OverallDifficulty = (float)overallDifficulty,
                // InfiniteTrainingPlayer가 사망 자체를 막으므로(CheckModsAllowFailure) 이 값은
                // 체력바 표시에만 영향을 준다. 설정으로 노출하지 않는다.
                DrainRate = 5,
                ApproachRate = 5,
            },
            Metadata = new BeatmapMetadata
            {
                Title = "Infinite Training",
                Artist = "LazerSR",
            },
            DifficultyName = $"{KEY_COUNT}K",
            // WorkingBeatmap.GetVirtualTrack()이 이 값 + 1000ms로 무음 트랙을 만든다.
            Length = SESSION_LENGTH_MS,
        };

        var beatmap = new ManiaBeatmap(new StageDefinition(KEY_COUNT))
        {
            BeatmapInfo = beatmapInfo,
        };

        // 타이밍 포인트가 하나도 없으면 판정/스크롤 기준이 기본값으로 떨어지므로 하나 깔아둔다.
        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

        for (int column = 0; column < KEY_COUNT; column++)
        {
            beatmap.HitObjects.Add(new Note { StartTime = FIRST_CHORD_TIME_MS, Column = column });
            beatmap.HitObjects.Add(new Note { StartTime = SESSION_LENGTH_MS, Column = column });
        }

        // osu! 본체가 제공하는 최소 구현 — 오디오/배경/스킨이 전부 null이고 각 소비처가 폴백을 갖고 있다.
        // 트랙은 null이므로 WorkingBeatmap.LoadTrack()이 무음 가상 트랙으로 대체한다.
        return new TestWorkingBeatmap(beatmap, null, audio);
    }
}
