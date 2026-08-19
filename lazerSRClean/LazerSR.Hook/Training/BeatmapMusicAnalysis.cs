using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;

namespace LazerSR.Hook.Training;

/// <summary>
/// 비트맵 하나가 무한 트레이닝 배경음으로 쓸 수 있는지 검사한 결과.
/// <see cref="Suitable"/>이 false면 <see cref="Message"/>가 그 이유다.
/// </summary>
public record MusicCandidate(
    bool Suitable,
    string Message,
    MusicTrackInfo? Track,
    double SpareMs);

/// <summary>
/// 선곡 화면에서 고른 난이도를 배경음 후보로 검사한다.
/// <para>
/// <b>펄스 bpm은 여기서 정하지 않는다.</b> 곡의 체감 펄스는 원본 bpm의 절반일 때가 많지만 아닐 때도
/// 있어서(등록했던 4곡 중 하나가 그랬다) 자동 판정이 불가능하다 — 추정값만 넣고 사용자가 고친다.
/// </para>
/// </summary>
public static class BeatmapMusicAnalysis
{
    /// <summary>프리뷰 지점 이후로 최소 이만큼은 남아 있어야 한다. 지속상승은 곡을 끊지 않고 이어서 튼다.</summary>
    public const double REQUIRED_SPARE_MS = 45_000;

    /// <summary>이 bpm 이상이면 체감 펄스를 절반으로 추정한다. 어디까지나 추정이고 사용자가 고칠 수 있다.</summary>
    private const double half_pulse_threshold = 180;

    /// <param name="trackLengthMs">오디오 길이. 아직 모르면 0 이하를 넘긴다.</param>
    public static MusicCandidate Analyse(WorkingBeatmap working, double trackLengthMs)
    {
        try
        {
            var info = working.BeatmapInfo;
            var metadata = info.Metadata;

            if (string.IsNullOrEmpty(metadata.AudioFile))
                return fail("오디오 파일 정보가 없다");

            var timingPoints = working.Beatmap.ControlPointInfo.TimingPoints;

            if (timingPoints.Count == 0)
                return fail("타이밍 정보가 없다");

            // 같은 bpm을 여러 번 찍어둔 맵이 흔하므로 개수가 아니라 서로 다른 박 길이를 센다.
            var lengths = new HashSet<double>();

            foreach (var point in timingPoints)
                lengths.Add(Math.Round(point.BeatLength, 3));

            if (lengths.Count > 1)
                return fail($"변속이 있다 (bpm {lengths.Count}종)");

            var timing = timingPoints[0];

            if (timing.TimeSignature.Numerator != 4)
                return fail($"4/4 박자가 아니다 ({timing.TimeSignature})");

            double bpm = timing.BPM;

            if (bpm <= 0)
                return fail("bpm을 읽을 수 없다");

            if (trackLengthMs <= 0)
                return fail("오디오 길이를 읽는 중");

            // 맵퍼가 프리뷰를 찍지 않으면 osu!는 길이의 40% 지점을 쓰는데, 그 지점은 마디와 아무 관계가 없다.
            // 원본 마디 시작에 정확히 찍혀 있다는 전제가 스냅의 근거이므로 미설정 맵은 아예 받지 않는다.
            if (metadata.PreviewTime < 0)
                return fail("미리듣기 구간이 지정돼 있지 않다");

            double previewMs = metadata.PreviewTime;

            double baseBpm = bpm >= half_pulse_threshold ? bpm / 2 : bpm;

            var track = new MusicTrackInfo(
                info.MD5Hash,
                metadata.AudioFile,
                audioHashOf(working),
                metadata.Artist,
                metadata.Title,
                info.DifficultyName,
                bpm,
                baseBpm,
                timing.Time,
                previewMs,
                0,
                true);

            // 실제로 곡이 시작되는 지점은 프리뷰가 아니라 거기에 가장 가까운 원본 마디 시작이다.
            double spare = trackLengthMs - MusicLibrary.SnappedStartMs(track);

            track = track with { SpareMs = spare };

            if (spare < REQUIRED_SPARE_MS)
                return fail($"프리뷰 이후 여유가 {spare / 1000:0}초뿐이다 ({REQUIRED_SPARE_MS / 1000:0}초 필요)");

            if (string.IsNullOrEmpty(track.AudioHash))
                return fail("오디오 파일을 찾을 수 없다");

            return new MusicCandidate(true, "사용 가능", track, spare);
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] BeatmapMusicAnalysis failed: {e}");

            return fail("분석에 실패했다");
        }
    }

    /// <summary>맵셋 안의 오디오 파일 해시. 같은 곡이 두 번 등록되는 것을 막는 유일한 키다.</summary>
    private static string audioHashOf(WorkingBeatmap working)
    {
        var set = working.BeatmapInfo.BeatmapSet;

        if (set == null)
            return string.Empty;

        return set.GetFile(working.BeatmapInfo.Metadata.AudioFile)?.File.Hash ?? string.Empty;
    }

    private static MusicCandidate fail(string reason) => new MusicCandidate(false, reason, null, 0);
}
