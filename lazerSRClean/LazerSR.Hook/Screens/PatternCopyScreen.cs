using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Screens;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens;
using osu.Game.Users;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 패턴 복제 모드의 진입 화면 — <b>화면을 그리지 않는 통과 지점</b>이다.
/// <para>
/// 무한 트레이닝은 대기 화면(<c>InfiniteTrainingScreen</c>)에서 패턴과 모드를 고른 뒤 시작하지만,
/// 패턴 복제는 고를 것이 없으므로 곧장 게임플레이("스케치북")로 들어간다.
/// 사용자 눈에는 메인 메뉴에서 바로 빈 맵이 뜨는 것으로 보인다.
/// </para>
/// <para>
/// 이 화면이 존재하는 이유는 <b>DI 때문</b>이다. <c>Beatmap</c>/<c>Ruleset</c>/<c>Mods</c>는
/// <see cref="OsuScreen"/>의 protected 멤버라, 메뉴 버튼 패치에서 직접 세팅하려면 리플렉션이 필요하다.
/// 화면을 하나 두면 그 전부가 평범한 프로퍼티 대입이 된다.
/// </para>
/// </summary>
public partial class PatternCopyScreen : OsuScreen
{
    /// <summary>
    /// 시드 비트맵의 판정창. 실제 주입 노트는 <c>ApplyDefaults</c>에 넘기는 난이도 객체가 따로 정하므로
    /// 이 값은 사실상 쓰이지 않는다 — 나중에 대상 게임에 맞춰 조정할 자리다.
    /// </summary>
    private const double seed_overall_difficulty = 8;

    /// <summary>서버에 활동 상태를 알리지 않는다 (<c>docs\guides\safety.md</c>).</summary>
    protected override UserActivity? InitialActivity => null;

    [Resolved]
    private RulesetStore rulesets { get; set; } = null!;

    [Resolved]
    private AudioManager audio { get; set; } = null!;

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        // 전환 연출이 끝난 뒤로 미룬다 — OnEntering 도중에는 아직 이 화면이 스택의 현재 화면이 아닐 수 있다.
        Schedule(enterSketchbook);
    }

    /// <summary>
    /// 게임플레이에서 돌아오면 이 화면에 머물 이유가 없다 — 곧장 메인 메뉴로 빠진다.
    /// (사용자 입장에서는 스케치북에서 ESC 한 번으로 메뉴까지 나가는 것으로 보인다.)
    /// </summary>
    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);

        this.Exit();
    }

    private void enterSketchbook()
    {
        try
        {
            if (!this.IsCurrentScreen())
                return;

            RulesetInfo? mania = rulesets.GetRuleset(@"mania");

            if (mania == null)
            {
                HookLog.Write("[LazerSR] PatternCopyScreen: mania ruleset not found.");
                this.Exit();
                return;
            }

            Beatmap.Value = PatternCopyBeatmap.Create(mania, audio, seed_overall_difficulty);
            Ruleset.Value = mania;
            Mods.Value = Array.Empty<Mod>();

            this.Push(new PatternCopyPlayerLoader());
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] PatternCopyScreen.enterSketchbook failed: {ex}");

            // 실패하면 아무것도 그리지 않는 화면에 갇히므로 반드시 빠져나간다.
            if (this.IsCurrentScreen())
                this.Exit();
        }
    }
}
