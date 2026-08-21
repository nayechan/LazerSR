using System;
using HarmonyLib;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using System.Linq;

namespace LazerSR.Hook.PatternCopy;

/// <summary>
/// 이미 주입된 롱노트의 길이를 <b>게임플레이 도중에</b> 줄인다.
/// <para>
/// 대상 게임의 롱노트는 언제 끝날지 미리 알 수 없어서 "아주 길게 깔아두고 끝이 확정되면 자르는" 방식이
/// 필수인데, 리드타임보다 긴 롱노트(=절대다수)는 <b>이미 헤드가 판정되어 붙잡고 있는 상태에서</b> 잘라야 한다.
/// </para>
///
/// <para>
/// <b>절대로 <c>ApplyDefaults()</c>를 다시 부르면 안 된다.</b> 그러면 <c>CreateNestedHitObjects()</c>가
/// Head/Tail/Body를 새 객체로 갈아치우고 <c>DrawableHitObject.onDefaultsApplied</c>가 전체 재적용을
/// 유발해서, 헤드의 판정 결과와 누적된 홀드 기록이 통째로 날아간다 — 절단이 아니라 노트 재생성이 되고
/// 붙잡고 있던 홀드가 끊긴다. (실기로 확인함.)
/// </para>
///
/// <para>
/// 대신 필요한 것만 외과적으로 고친다:
/// <list type="number">
/// <item><c>HoldNote.Duration</c> 대입 — setter가 <b>기존</b> <c>Tail.StartTime</c>을 갱신한다(객체 교체 없음).
///       Tail의 lifetime은 <c>StartTimeBindable</c>을 타고 자동으로 따라온다.</item>
/// <item><c>Body.Duration</c> 대입 — <c>Duration</c> setter가 Body는 건드리지 않아 수동으로 맞춘다.</item>
/// <item><c>DrawableHitObject.DefaultsApplied</c> 강제 발화 — 화면상 길이는
///       <c>ScrollingHitObjectContainer</c>가 <c>layoutComputed</c>에 캐시하므로 무효화해야 한다.
///       <b>게임플레이 중 이 이벤트의 구독자는 <c>invalidateHitObject</c> 하나뿐</b>이라
///       (나머지는 에디터 전용) 부작용 없이 레이아웃 재계산만 얻는다.</item>
/// </list>
/// field-like 이벤트를 밖에서 발화하는 리플렉션은 <c>docs\guides\ui-patching.md</c> §3의 검증된 패턴이다.
/// </para>
/// </summary>
internal static class HoldNoteTruncator
{
    /// <summary>
    /// <paramref name="note"/>의 길이를 <paramref name="newDuration"/>으로 줄인다.
    /// 이미 지나간 시각으로는 자르지 않는다(테일이 과거가 되면 즉시 강제 미스가 된다).
    /// </summary>
    public static void Truncate(Playfield playfield, HoldNote note, double newDuration, double currentTimeMs)
    {
        // 테일이 현재 시각보다 앞서면 판정 자체가 불가능하다 — 최소한 지금 이후로 민다.
        double earliest = currentTimeMs - note.StartTime;
        if (newDuration < earliest)
            newDuration = earliest;

        if (newDuration < 0)
            newDuration = 0;

        // 1) Tail.StartTime은 이 setter가 기존 객체에 직접 반영한다.
        note.Duration = newDuration;

        // 2) Body는 Duration setter가 건드리지 않는다.
        if (note.Body != null)
            note.Body.Duration = newDuration;

        // 3) 화면상 길이 캐시 무효화.
        InvalidateLayout(playfield, note);
    }

    private static void InvalidateLayout(Playfield playfield, HoldNote note)
    {
        // 풀링 때문에 살아 있는 동안만 드로어블이 존재한다. 없으면 아직 레이아웃도 없어서 할 일이 없다.
        var drawable = playfield.AllHitObjects.FirstOrDefault(d => ReferenceEquals(d.HitObject, note));

        if (drawable == null)
            return;

        var handler = AccessTools.Field(typeof(DrawableHitObject), nameof(DrawableHitObject.DefaultsApplied))
                                 ?.GetValue(drawable) as Action<DrawableHitObject>;

        handler?.Invoke(drawable);
    }
}
