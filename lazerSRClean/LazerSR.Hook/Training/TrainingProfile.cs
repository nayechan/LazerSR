using System;
using System.Collections.Generic;

namespace LazerSR.Hook.Training;

/// <summary>
/// 세부패턴별 실력 기록과 선택 상태. **세션 한정** — 저장/불러오기는 미구현이라 osu!를 끄면 사라진다.
/// <para>
/// 단기 bpm은 <b>시작값이자 결과값</b>이다. 처음에는 <see cref="SubPattern.StartBpm"/>으로 채워지고,
/// 대기 화면에서 사용자가 스냅 단위로 조정할 수 있으며, 측정을 마치고 "저장"을 누르면 확정값으로 덮인다.
/// </para>
/// </summary>
public static class TrainingProfile
{
    private const double min_bpm = 10;
    private const double max_bpm = 1000;

    private static readonly Dictionary<string, double> shortTermBpm = new Dictionary<string, double>();
    private static readonly HashSet<string> enabled = new HashSet<string>();

    /// <summary>값이 바뀔 때마다 발화. 대기 화면이 표시를 갱신하는 데 쓴다.</summary>
    public static event Action? Changed;

    public static double GetShortTermBpm(SubPattern subPattern)
    {
        if (shortTermBpm.TryGetValue(subPattern.Id, out double bpm))
            return bpm;

        return subPattern.StartBpm;
    }

    public static void SetShortTermBpm(SubPattern subPattern, double bpm)
    {
        shortTermBpm[subPattern.Id] = Math.Clamp(bpm, min_bpm, max_bpm);
        Changed?.Invoke();
    }

    /// <summary>스냅 배수만큼 올리거나 내린다. 대기 화면의 −/+ 버튼이 쓴다.</summary>
    public static void StepShortTermBpm(SubPattern subPattern, int direction) =>
        SetShortTermBpm(subPattern, GetShortTermBpm(subPattern) + direction * subPattern.BpmSnap);

    public static bool IsEnabled(SubPattern subPattern) => enabled.Contains(subPattern.Id);

    public static void SetEnabled(SubPattern subPattern, bool value)
    {
        if (value)
            enabled.Add(subPattern.Id);
        else
            enabled.Remove(subPattern.Id);

        Changed?.Invoke();
    }

    /// <summary>측정 대상. 순서는 카탈로그 순이며, 실제 측정 순서는 시작 시 셔플된다.</summary>
    public static List<SubPattern> EnabledSubPatterns()
    {
        var result = new List<SubPattern>();

        foreach (var subPattern in PatternCatalog.AllSubPatterns)
        {
            if (enabled.Contains(subPattern.Id))
                result.Add(subPattern);
        }

        return result;
    }
}
