using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace LazerSR.Hook.Calculators;

/// <summary>
/// 결과창 산점도에서 사용자가 드래그로 잡은 구간을 분석한다. 확정된 스코어와 변환 완료 보면을
/// 읽기만 하며 아무것도 쓰지 않는다.
/// </summary>
internal static class ManiaSectionAnalysis
{
    /// <summary>
    /// 구간 정확도 3종. 셋 다 같은 판정 집합을 쓰고 <b>320(Perfect) 판정의 가중치와 분모만 다르다.</b>
    /// 레거시(구클라)는 320을 300과 동일 취급하고, lazer는 305, pp 공식은 320으로 센다.
    /// 구간에 판정이 없으면 <c>null</c>.
    /// </summary>
    public readonly record struct SectionAccuracy(double? Legacy, double? Lazer, double? Pp);

    /// <summary>판정 하나가 정확도에 기여하는 값. 미스는 0이지만 분모에는 들어가므로 걸러내지 않는다.</summary>
    private static int weightFor(HitResult result, int perfectWeight) => result switch
    {
        HitResult.Perfect => perfectWeight,
        HitResult.Great => 300,
        HitResult.Good => 200,
        HitResult.Ok => 100,
        HitResult.Meh => 50,
        _ => 0,
    };

    /// <summary>
    /// <paramref name="events"/>는 <see cref="HitResultExtensions.IsBasic"/>인 판정만 담고 있어야 한다
    /// (롱노트 바디 같은 부가 판정은 정확도에 들어가지 않는다). <b>미스는 반드시 포함되어야 한다.</b>
    /// </summary>
    public static SectionAccuracy Accuracy(IReadOnlyList<HitEvent> events)
    {
        if (events.Count == 0)
            return new SectionAccuracy(null, null, null);

        long legacy = 0;
        long lazer = 0;
        long pp = 0;

        foreach (var e in events)
        {
            legacy += weightFor(e.Result, 300);
            lazer += weightFor(e.Result, 305);
            pp += weightFor(e.Result, 320);
        }

        return new SectionAccuracy(
            legacy / (300.0 * events.Count),
            lazer / (305.0 * events.Count),
            pp / (320.0 * events.Count));
    }

    /// <summary>정확도 표기 문자열. 값이 없으면 대시.</summary>
    public static string Format(double? accuracy) => accuracy is { } value ? value.ToString("P2") : "-";

    /// <summary>구간에 속하는 판정만 고른다. 점이 찍히는 위치와 같은 기준(<c>GetEndTime</c>)을 쓴다.</summary>
    public static List<HitEvent> EventsWithin(IReadOnlyList<HitEvent> events, double startTime, double endTime)
    {
        var result = new List<HitEvent>();

        foreach (var e in events)
        {
            double time = e.HitObject.GetEndTime();

            if (time >= startTime && time <= endTime)
                result.Add(e);
        }

        return result;
    }

    /// <summary>
    /// 구간 안의 노트만 담은 임시 <see cref="ManiaBeatmap"/>. 메모리에만 존재하며 realm·파일에 닿지 않는다.
    /// 난이도 오브젝트가 만들어지지 않는 크기(2노트 미만)면 <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <b>노트는 쪼갤 수 없으므로 <see cref="HitObject.StartTime"/>을 소속 기준으로 쓴다</b> — 경계에 걸친
    /// 롱노트는 통째로 포함되거나 통째로 빠진다. 정확도 쪽은 헤드/테일을 각자의 시각으로 따로 세므로
    /// 경계에서 두 기준이 1노트 정도 어긋날 수 있지만, 구간 자체가 사용자의 손끝으로 정해지는 값이라
    /// 난이도·정확도 어느 쪽에도 의미 있는 차이를 만들지 않는다.
    /// <para>
    /// <c>BeatmapInfo</c>는 원본 참조를 그대로 공유한다 — sunny 계산이 여기서 읽는 것은
    /// <c>Difficulty.OverallDifficulty</c> 하나뿐이고 쓰기는 없다. 히트오브젝트도 같은 이유로 복제하지 않는다.
    /// </para>
    /// </remarks>
    public static ManiaBeatmap? BuildSection(IBeatmap source, double startTime, double endTime)
    {
        if (source is not ManiaBeatmap mania || mania.Stages.Count == 0)
            return null;

        var section = new ManiaBeatmap(mania.Stages[0])
        {
            BeatmapInfo = mania.BeatmapInfo,
        };

        for (int i = 1; i < mania.Stages.Count; i++)
            section.Stages.Add(mania.Stages[i]);

        foreach (var hitObject in mania.HitObjects)
        {
            if (hitObject.StartTime >= startTime && hitObject.StartTime <= endTime)
                section.HitObjects.Add(hitObject);
        }

        return section.HitObjects.Count < 2 ? null : section;
    }
}
