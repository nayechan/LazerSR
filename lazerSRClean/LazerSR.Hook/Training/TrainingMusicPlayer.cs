using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;

namespace LazerSR.Hook.Training;

/// <summary>
/// 무한 트레이닝의 배경음. <b>게임플레이 시계와 분리된 독립 채널</b>로 재생한다.
/// </summary>
/// <remarks>
/// <para>
/// 게임플레이 시계의 소스 트랙(무음 가상 트랙)을 갈아끼우는 방법도 있지만, 그 경로에서는
/// 곡의 재생 위치가 곧 게임플레이 시각이 되어 <b>세션 시각(최대 83분)과 곡 길이(2~5분)가 어긋난다.</b>
/// 그래서 osu! 본체의 <c>BackgroundMusicManager</c>(랭크드 플레이)와 같은 방식으로
/// <see cref="DrawableTrack"/>을 따로 들고 재생한다.
/// </para>
/// <para>
/// <b>정밀 시작은 불가능하므로 인과를 뒤집는다.</b> 트랙을 정확한 시각에 시작할 수는 없으니
/// (BASS 시작 지연이 일정하지 않다), 먼저 트랙을 돌리고 <b>실제 재생 위치를 읽어
/// "다음 다운비트가 오는 게임플레이 시각"을 계산</b>해 그것을 세트 시작 시각으로 쓴다.
/// 세트마다 다시 맞추므로 드리프트가 누적될 구간이 없다.
/// </para>
/// <para>
/// 배속은 <c>Tempo</c>로 건다 — 피치가 유지된다(osu!의 배속 mod 기본값과 같다).
/// 패턴 bpm이 바뀌면 <b>그 bpm을 쓰는 세트가 시작하는 시각에</b> 배속을 바꿔야 박자가 어긋나지 않으므로,
/// 즉시 적용하지 않고 <see cref="ScheduleRate"/>로 예약한다.
/// </para>
/// </remarks>
public partial class TrainingMusicPlayer : CompositeDrawable
{
    /// <summary>시작·전환 시 클릭음을 막는 페이드.</summary>
    private const double fade_ms = 150;

    /// <summary>마지막 세트가 끝나고 이만큼 지나면 음악을 끈다(휴식 모드·세션 종료).</summary>
    private const double idle_stop_ms = 1500;

    /// <summary>
    /// 트랙이 이 시간 안에 실제로 돌기 시작하지 않으면 음악을 포기한다.
    /// <b>이 가드가 없으면 세트 편성이 영원히 막혀 노트가 아예 생성되지 않는다.</b>
    /// </summary>
    private const double start_timeout_ms = 3000;

    private readonly BindableDouble tempo = new BindableDouble(1);
    private readonly List<ScheduledRate> pendingRates = new List<ScheduledRate>();

    private DrawableTrack? track;
    private MusicTrackInfo? currentInfo;

    private double songStartMs;
    private double currentRate = 1;
    private bool awaitingAlignment;
    private bool started;
    private double alignmentDeadlineMs;
    private bool disabled;
    private double lastSetEndMs = double.NegativeInfinity;
    private int lastPickedIndex = -1;
    private readonly List<int> bag = new List<int>();
    private readonly Random rng = new Random();

    [Resolved(canBeNull: true)]
    private BeatmapManager? beatmaps { get; set; }

    /// <summary>등록된 곡이 하나도 없으면 음악 없이 동작한다.</summary>
    public bool Available => MusicLibrary.Tracks.Count > 0;

    /// <summary>
    /// 지금 음악을 쓸 수 있는가. 로드 실패나 시작 지연으로 한 번 꺼지면 세션 내내 꺼진 채로 둔다 —
    /// 호출부가 이걸 보고 <b>정렬 없이 세트를 편성</b>하므로 음악 문제가 훈련을 멈추지 않는다.
    /// </summary>
    public bool Usable => Available && !disabled && beatmaps != null;

    /// <summary>지금 곡이 실제로 흐르고 있는가. 지속상승은 이게 참이면 곡을 바꾸지 않는다.</summary>
    public bool IsPlaying => track != null && track.IsRunning;

    /// <summary>
    /// 새 곡을 시작하고, 준비되면 <b>곡의 다운비트에 맞춘 세트 시작 시각</b>을 돌려준다.
    /// 트랙이 아직 돌기 시작하지 않았으면 <c>false</c>를 돌려주므로 호출부는 다음 프레임에 다시 부르면 된다.
    /// </summary>
    /// <param name="patternBpm">이 세트의 패턴 bpm. 배속이 여기서 정해진다.</param>
    /// <param name="earliestStartMs">시퀀서가 확보해야 하는 최소 시작 시각(선행 주입 시간 포함).</param>
    /// <param name="nowMs">현재 게임플레이 시각.</param>
    public bool TryBeginSong(double patternBpm, double earliestStartMs, double nowMs, out double alignedStartMs)
    {
        alignedStartMs = 0;

        if (!Usable)
            return false;

        if (!awaitingAlignment)
        {
            beginSong(patternBpm);
            alignmentDeadlineMs = nowMs + start_timeout_ms;
        }

        // 드로어블이 로드된 뒤에야 seek/start가 먹는다 (osu! BackgroundMusicManager와 같은 가드).
        if (track != null && !started && track.IsLoaded)
        {
            track.Seek(songStartMs);
            track.VolumeTo(0).VolumeTo(1, fade_ms);
            track.Start();
            started = true;
        }

        // 재생이 실제로 진행되기 시작한 뒤에야 위치를 신뢰할 수 있다.
        bool running = started && track != null && track.IsRunning && track.CurrentTime > songStartMs;

        if (!running)
        {
            if (nowMs > alignmentDeadlineMs)
            {
                HookLog.Write("[LazerSR] TrainingMusicPlayer gave up waiting for playback; continuing without music.");
                disabled = true;
                awaitingAlignment = false;
                stopTrack();
            }

            return false;
        }

        double songNow = track!.CurrentTime;

        double measureMs = MusicLibrary.MeasureMs(currentInfo!);
        double minSongMs = songNow + Math.Max(0, earliestStartMs - nowMs) * currentRate;
        double index = Math.Ceiling((minSongMs - songStartMs) / measureMs);

        double targetSongMs = songStartMs + index * measureMs;

        alignedStartMs = nowMs + (targetSongMs - songNow) / currentRate;
        awaitingAlignment = false;

        return true;
    }

    /// <summary>
    /// 세트가 편성됐음을 알린다. 배속은 <b>그 세트가 시작하는 시각에</b> 적용된다 —
    /// 미리 잡아둔 세트가 아직 흐르는 중에 배속을 바꾸면 박자가 어긋난다.
    /// </summary>
    public void RegisterSet(double startMs, double endMs, double patternBpm)
    {
        lastSetEndMs = Math.Max(lastSetEndMs, endMs);

        if (currentInfo == null)
            return;

        pendingRates.Add(new ScheduledRate(startMs, MusicLibrary.RateFor(currentInfo, patternBpm)));
    }

    public void Update(double nowMs)
    {
        applyPendingRates(nowMs);

        if (track != null && !awaitingAlignment && nowMs > lastSetEndMs + idle_stop_ms)
            stopTrack();
    }

    public void StopAll()
    {
        pendingRates.Clear();
        awaitingAlignment = false;
        stopTrack();
    }

    private void beginSong(double patternBpm)
    {
        MusicTrackInfo? info = null;
        Track? loaded = null;

        // 참조 방식이라 맵이 지워지면 그 곡만 사라진다 — 한 곡이 안 열린다고 세션 음악을 끄지 않고
        // 다른 곡을 집어본다. 전부 실패해야 비로소 음악 없이 진행한다.
        for (int attempt = 0; attempt < MusicLibrary.Tracks.Count && loaded == null; attempt++)
        {
            info = pickTrack();
            loaded = MusicLibrary.LoadTrack(beatmaps!, info);

            if (loaded == null)
                HookLog.Write($"[LazerSR] TrainingMusicPlayer could not load {info.Title}; trying another song.");
        }

        if (info == null || loaded == null)
        {
            disabled = true;

            return;
        }

        stopTrack();

        currentInfo = info;
        songStartMs = MusicLibrary.SnappedStartMs(info);
        currentRate = MusicLibrary.RateFor(info, patternBpm);
        tempo.Value = currentRate;

        track = new DrawableTrack(loaded);
        track.AddAdjustment(AdjustableProperty.Tempo, tempo);

        AddInternal(track);

        started = false;
        awaitingAlignment = true;
    }

    /// <summary>예약 순서(시각 오름차순)대로 적용해야 여러 개가 동시에 도래해도 마지막 것이 남는다.</summary>
    private void applyPendingRates(double nowMs)
    {
        while (pendingRates.Count > 0 && pendingRates[0].AtMs <= nowMs)
        {
            currentRate = pendingRates[0].Rate;
            tempo.Value = currentRate;
            pendingRates.RemoveAt(0);
        }
    }

    private void stopTrack()
    {
        if (track == null)
            return;

        track.Stop();
        RemoveInternal(track, true);
        track = null;
        currentInfo = null;
        started = false;
    }

    /// <summary>봉지에서 하나 꺼낸다. 봉지가 비면 다시 섞되 직전 곡이 연달아 나오지 않게 한다.</summary>
    private MusicTrackInfo pickTrack()
    {
        var all = MusicLibrary.Tracks;

        if (bag.Count == 0)
        {
            for (int i = 0; i < all.Count; i++)
                bag.Add(i);

            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            // 곡이 2개 이상일 때만 경계 중복을 피할 수 있다.
            if (bag.Count > 1 && bag[0] == lastPickedIndex)
                (bag[0], bag[bag.Count - 1]) = (bag[bag.Count - 1], bag[0]);
        }

        lastPickedIndex = bag[0];
        bag.RemoveAt(0);

        return all[lastPickedIndex];
    }

    private record ScheduledRate(double AtMs, double Rate);
}
