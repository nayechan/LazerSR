using osu.Framework.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Tests.Beatmaps;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 패턴 복제 모드의 시드 비트맵. 파일에서 읽지 않고 코드로 만든다.
/// <para>
/// 무한 트레이닝의 <see cref="InfiniteTrainingBeatmap"/>과 성격이 같고 <b>6키</b>라는 점만 다르다.
/// 두 모드는 서로를 참조하지 않는다.
/// </para>
/// <para>
/// 컬럼 6개인 이유: 대상 게임의 4레인이 osu! 2~5번 키로, 좌/우 사이드노트가 1번/6번 키로 간다
/// (레인0·1 동시 SIDE=왼쪽, 레인2·3 동시 SIDE=오른쪽). 컬럼 수는 세션 도중 바꿀 수 없으므로
/// 처음부터 6으로 시작한다.
/// </para>
/// </summary>
internal static class PatternCopyBeatmap
{
    /// <summary>키 수. 컬럼 수는 <c>ManiaPlayfield</c>/<c>Stage</c> 생성자에서 고정되므로 여기서만 결정된다.</summary>
    public const int KEY_COUNT = 6;

    /// <summary>진입용 첫 동시치기 시각.</summary>
    public const double FIRST_CHORD_TIME_MS = 1000;

    /// <summary>맵 길이. 무음 가상 트랙 길이도 여기서 파생된다 (약 83분).</summary>
    public const double SESSION_LENGTH_MS = 5_000_000;

    /// <param name="overallDifficulty">
    /// 시드 노트의 판정창. 실제로 주입되는 노트는 <c>ApplyDefaults</c>에 넘기는 난이도 객체가
    /// 따로 정하므로(<c>HitObject.ApplyDefaultsToSelf</c> → <c>HitWindows.SetDifficulty</c>)
    /// 이 값은 사실상 쓰이지 않는다.
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
                // PatternCopyPlayer가 사망 자체를 막으므로(CheckModsAllowFailure) 체력바 표시에만 영향을 준다.
                DrainRate = 5,
                ApproachRate = 5,
            },
            Metadata = new BeatmapMetadata
            {
                Title = "Pattern Copy",
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

        // 노트가 0개면 진입 자체가 불가능하다 — Player.LoadedBeatmapSuccessfully가
        // DrawableRuleset.Objects.Any()로 판정하고, false면 PlayerLoader가 즉시 exit한다.
        //
        // 무한 트레이닝과 동일하게 앞뒤로 하나씩 깐다. 앞의 동시치기는 진입용이고,
        // 뒤의 것은 맵 길이(= 무음 트랙 길이)를 확보하기 위한 것이다.
        // 모드를 켠 뒤 대상 게임을 세팅하는 데 시간이 걸리므로 초반 동시치기는 지나가버려 방해되지 않는다.
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
