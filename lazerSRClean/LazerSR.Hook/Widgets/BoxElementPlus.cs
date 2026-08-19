using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Screens.Play;
using osu.Game.Skinning.Components;

namespace LazerSR.Hook.Widgets;

/// <summary>
/// osu! 기본 <see cref="BoxElement"/>에 상황별 자동 숨김 두 가지를 더한 것.
///
/// <para>
/// <b>기존 <see cref="BoxElement"/>에 설정을 끼워 넣는 것은 불가능하다</b> — HarmonyX는 메서드 본체를
/// 바꿀 뿐 타입에 프로퍼티를 추가하지 못한다. 그래서 상속해서 <c>[SettingSource]</c>를 덧붙인다.
/// 기존 위젯의 기능(색상 팔레트, 모서리 둥글기)은 상속으로 그대로 따라온다.
/// </para>
///
/// <para>
/// 숨김은 <c>Alpha</c>가 아니라 <c>Colour</c>의 투명도로 처리한다. <c>Alpha = 0</c>은
/// <c>IsPresent</c>를 false로 만들어 스킨 에디터에서 선택조차 할 수 없게 되기 때문이다.
/// </para>
/// </summary>
public partial class BoxElementPlus : BoxElement
{
    [SettingSource("일시정지 대응", "일시정지 중과 재개 카운트다운 동안 완전히 투명해진다")]
    public BindableBool HideWhilePaused { get; } = new BindableBool();

    [SettingSource("SV 대응", "채보의 SV가 아래 기준 미만인 구간에서 완전히 투명해진다")]
    public BindableBool HideOnLowScrollSpeed { get; } = new BindableBool();

    /// <summary>이 SV 미만이면 "느린 구간"으로 본다. 1.0이 보통 속도다.</summary>
    [SettingSource("SV 기준값", "이 값보다 SV가 낮으면 숨긴다")]
    public BindableNumber<float> LowScrollSpeedThreshold { get; } =
        new BindableFloat(0.5f) { MinValue = 0.05f, MaxValue = 2f, Precision = 0.05f };

    [Resolved(CanBeNull = true)]
    private IGameplayClock? gameplayClock { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    private ControlPointInfo? controlPoints;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        controlPoints = gameplayState?.Beatmap.ControlPointInfo;
    }

    protected override void Update()
    {
        base.Update();

        Colour = shouldHide() ? AccentColour.Value.Opacity(0f) : AccentColour.Value;
    }

    private bool shouldHide()
    {
        // 재개 카운트다운(DelayedResumeOverlay) 동안에도 게임플레이 시계는 아직 멈춰 있다.
        // Player.Resume()이 카운트다운이 끝난 뒤에야 시계를 시작하기 때문에, 이 검사 하나로 둘 다 덮인다.
        if (HideWhilePaused.Value && gameplayClock?.IsPaused.Value == true)
            return true;

        // 채보의 SV(초록선) 값만 본다. mania는 BPM 변화도 실제 스크롤에 영향을 주지만,
        // 여기서 보려는 것은 "맵이 지정한 스크롤 속도"이므로 의도적으로 제외한다.
        if (HideOnLowScrollSpeed.Value && controlPoints != null)
            return controlPoints.EffectPointAt(Time.Current).ScrollSpeed < LowScrollSpeedThreshold.Value;

        return false;
    }
}
