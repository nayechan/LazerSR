using System;
using System.Collections.Generic;
using osu.Framework.Audio.Track;
using osu.Game.Beatmaps;

namespace LazerSR.Hook.Training;

/// <summary>
/// 배경음 한 곡. 오디오는 <b>복사하지 않고 참조</b>하므로 실제 경로가 아니라 식별자를 들고 있다.
/// </summary>
/// <param name="BeatmapMd5">등록에 쓴 난이도의 MD5. 재생 직전에 이걸로 realm에서 맵을 다시 찾는다.</param>
/// <param name="AudioFile">맵셋 안의 오디오 파일 이름. 표시와 경로 해결에 쓴다.</param>
/// <param name="AudioHash">오디오 파일 해시. <b>중복 등록 판정의 유일한 키</b>다.</param>
/// <param name="DifficultyName">등록에 쓴 난이도 이름. 목록에서 곡을 구분하는 표시용이다.</param>
/// <param name="SpareMs">시작 지점 이후 남은 오디오 길이. 등록 시점에 계산해 그대로 들고 있는다.</param>
/// <param name="Enabled">훈련에서 이 곡을 쓸지. 목록의 체크박스가 정한다.</param>
/// <param name="Bpm">원본 bpm. <b>박자 그리드의 기준</b>이며 마디 스냅이 여기서 나온다.</param>
/// <param name="BaseBpm">체감 펄스 bpm. <b>배속 계산의 기준</b>이고 사용자가 직접 정한다.</param>
/// <param name="OffsetMs">첫 다운비트 시각.</param>
/// <param name="PreviewMs">곡을 시작할 지점의 기준. 가장 가까운 원본 마디 시작으로 스냅된다.</param>
public record MusicTrackInfo(
    string BeatmapMd5,
    string AudioFile,
    string AudioHash,
    string Artist,
    string Title,
    string DifficultyName,
    double Bpm,
    double BaseBpm,
    double OffsetMs,
    double PreviewMs,
    double SpareMs,
    bool Enabled);

/// <summary>
/// 패턴 bpm에 맞춰 곡을 트는 데 필요한 계산. 곡 목록 자체는 <see cref="TrainingMusicStore"/>가 갖는다.
/// </summary>
public static class MusicLibrary
{
    /// <summary>
    /// 배속 허용 범위. osu! 자신이 쓰는 범위와 같다 — 하프타임 하한 0.5, <c>UserPlaybackRate</c> 상한 2.0.
    /// 이 밖으로 나가면 옥타브를 한 칸 옮긴다.
    /// </summary>
    public const double MIN_RATE = 0.5;

    public const double MAX_RATE = 2.0;

    /// <summary>
    /// 실제로 재생 대상이 되는 곡. <b>체크가 꺼진 곡은 여기 들어오지 않는다</b> —
    /// 목록 전체가 필요하면 <see cref="TrainingMusicStore.Songs"/>를 본다.
    /// </summary>
    public static IReadOnlyList<MusicTrackInfo> Tracks => enabledTracks ??= collectEnabled();

    private static IReadOnlyList<MusicTrackInfo>? enabledTracks;

    static MusicLibrary()
    {
        // 이 판정은 세트를 편성할 때마다 돌므로 목록이 바뀔 때만 다시 만든다.
        TrainingMusicStore.Changed += () => enabledTracks = null;
    }

    private static IReadOnlyList<MusicTrackInfo> collectEnabled()
    {
        var result = new List<MusicTrackInfo>();

        foreach (var song in TrainingMusicStore.Songs)
        {
            if (song.Enabled)
                result.Add(song);
        }

        return result;
    }

    /// <summary>
    /// 등록된 식별자로 realm에서 맵을 다시 찾아 트랙을 얻는다 — 절대 경로를 저장하지 않는 이유가 이것이다.
    /// 맵이 지워졌거나 오디오가 없으면 <c>null</c>.
    /// </summary>
    /// <remarks>
    /// 오디오 파일이 없을 때 <c>WorkingBeatmap.LoadTrack()</c>은 null이 아니라 <b>1초짜리 무음 가상 트랙</b>을
    /// 돌려준다. 그래서 로드 전에 맵셋에 그 파일이 실제로 있는지 먼저 확인한다.
    /// </remarks>
    public static Track? LoadTrack(BeatmapManager beatmaps, MusicTrackInfo info)
    {
        var beatmapInfo = Resolve(beatmaps, info);

        if (beatmapInfo == null)
            return null;

        try
        {
            return beatmaps.GetWorkingBeatmap(beatmapInfo).LoadTrack();
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] MusicLibrary.LoadTrack({info.Title}) failed: {e}");

            return null;
        }
    }

    /// <summary>등록된 곡의 원본 비트맵. 맵이 지워졌거나 오디오 파일이 없으면 <c>null</c>.</summary>
    public static BeatmapInfo? Resolve(BeatmapManager beatmaps, MusicTrackInfo info)
    {
        try
        {
            var beatmapInfo = beatmaps.QueryBeatmap(b => b.MD5Hash == info.BeatmapMd5);

            return beatmapInfo?.BeatmapSet?.GetFile(info.AudioFile) == null ? null : beatmapInfo;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] MusicLibrary.Resolve({info.Title}) failed: {e}");

            return null;
        }
    }

    /// <summary>
    /// 원본 비트맵이 사라진 곡을 목록에서 걷어낸다. 참조 방식이라 맵을 지우면 곡도 없어지므로,
    /// 무한 트레이닝 대기 화면에 들어올 때 한 번 정리한다.
    /// </summary>
    public static void PruneMissing(BeatmapManager beatmaps)
    {
        foreach (var song in new List<MusicTrackInfo>(TrainingMusicStore.Songs))
        {
            if (Resolve(beatmaps, song) != null)
                continue;

            HookLog.Write($"[LazerSR] MusicLibrary dropping {song.Title} — source beatmap is gone.");
            TrainingMusicStore.Remove(song);
        }
    }

    /// <summary>
    /// 배속 계산에 쓸 기준 bpm. <see cref="MusicTrackInfo.BaseBpm"/>을 그대로 쓰되,
    /// 배속이 허용 범위를 벗어날 때만 2의 거듭제곱으로 옮긴다 — 2배수는 박자 그리드를 보존하므로
    /// 정렬에 영향을 주지 않는다.
    /// </summary>
    public static double EffectiveBpm(MusicTrackInfo track, double patternBpm)
    {
        if (track.BaseBpm <= 0 || patternBpm <= 0)
            return Math.Max(1, track.BaseBpm);

        double effective = track.BaseBpm;

        // 상한/하한을 벗어나는 동안만 옮긴다. 두 루프는 동시에 돌 수 없다.
        while (patternBpm / effective > MAX_RATE)
            effective *= 2;

        while (patternBpm / effective < MIN_RATE)
            effective /= 2;

        return effective;
    }

    public static double RateFor(MusicTrackInfo track, double patternBpm) => patternBpm / EffectiveBpm(track, patternBpm);

    /// <summary>원본 마디 길이(ms). 4/4 기준.</summary>
    public static double MeasureMs(MusicTrackInfo track) => 4 * 60000.0 / track.Bpm;

    /// <summary>프리뷰 타임에 가장 가까운 원본 마디 시작. 곡을 여기서부터 튼다.</summary>
    public static double SnappedStartMs(MusicTrackInfo track)
    {
        double measureMs = MeasureMs(track);
        double index = Math.Round((track.PreviewMs - track.OffsetMs) / measureMs);

        return track.OffsetMs + Math.Max(0, index) * measureMs;
    }
}
