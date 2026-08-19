using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Skinning;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 원본 비트맵에서 <b>구간 밖 노트만 걷어낸</b> 사본. 곡·배경·스킨·타이밍·난이도는 전부 원본 것을 그대로 쓴다.
/// <para>
/// 노트를 실제로 지우기 때문에 osu!가 보기에 그냥 "짧은 맵"이 된다 — 시작 지점도, 완료 판정도,
/// 점수·정확도 집계도 전부 osu!의 평소 경로가 알아서 맞춘다. 구간 이전 노트를 미리 판정으로
/// 채우거나 강제 미스를 막는 보정이 일절 필요 없다.
/// </para>
/// <para>
/// <b><see cref="BeatmapInfo"/>를 원본과 공유한다.</b> 그래야 <c>WorkingBeatmap.TryTransferTrack</c>의
/// <c>AudioEquals</c> 검사가 통과해 <c>MusicController</c>가 <b>이미 로드된 트랙 인스턴스를 그대로 넘겨준다</b> —
/// 오디오를 다시 로드하지 않고, 결과창으로 돌아갈 때도 같은 경로로 되돌아간다.
/// </para>
/// </summary>
public sealed class SectionPracticeBeatmap : WorkingBeatmap
{
    /// <summary>
    /// <c>GetBeatmapTrack</c>은 protected라 밖에서 부를 수 없다. 보통은 <c>TryTransferTrack</c>으로
    /// 트랙을 넘겨받으므로 호출될 일이 없지만, <c>LoadTrack()</c>이 불리는 경로를 대비해 잡아둔다.
    /// 못 찾으면 무음 가상 트랙으로 떨어진다(<c>WorkingBeatmap.LoadTrack</c>의 폴백).
    /// </summary>
    private static readonly MethodInfo? get_beatmap_track =
        AccessTools.Method(typeof(WorkingBeatmap), "GetBeatmapTrack");

    private readonly WorkingBeatmap source;
    private readonly double startTime;
    private readonly double endTime;

    /// <param name="startTime">이 시각부터의 노트를 남긴다 (<see cref="HitObject.StartTime"/> 기준).</param>
    /// <param name="endTime">이 시각까지의 노트를 남긴다. 경계에 걸친 롱노트는 통째로 살아남는다.</param>
    public SectionPracticeBeatmap(WorkingBeatmap source, double startTime, double endTime, AudioManager audio)
        : base(source.BeatmapInfo, audio)
    {
        this.source = source;
        this.startTime = startTime;
        this.endTime = endTime;
    }

    protected override IBeatmap GetBeatmap()
    {
        // Clone()은 MemberwiseClone이라 HitObjects 리스트가 원본과 공유된다.
        // 반드시 새 리스트로 갈아끼워야 원본 비트맵이 오염되지 않는다.
        var beatmap = source.Beatmap.Clone();

        var property = AccessTools.Property(beatmap.GetType(), "HitObjects");

        if (property?.GetValue(beatmap) is not IList original || Activator.CreateInstance(property.PropertyType) is not IList filtered)
        {
            HookLog.Write("[LazerSR] SectionPracticeBeatmap: could not replace HitObjects, playing the full map.");
            return beatmap;
        }

        foreach (object? item in original)
        {
            if (item is HitObject hitObject && hitObject.StartTime >= startTime && hitObject.StartTime <= endTime)
                filtered.Add(hitObject);
        }

        property.SetValue(beatmap, filtered);

        return beatmap;
    }

    public override Texture GetBackground() => source.GetBackground();

    public override Texture GetPanelBackground() => source.GetPanelBackground();

    // 다른 어셈블리에서 protected internal 멤버를 재정의할 때는 protected로 선언해야 한다.
    protected override ISkin GetSkin() => source.Skin;

    public override Stream GetStream(string storagePath) => source.GetStream(storagePath);

    protected override Track GetBeatmapTrack() =>
        get_beatmap_track?.Invoke(source, Array.Empty<object>()) as Track ?? GetVirtualTrack(1000);
}
