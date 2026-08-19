using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osuTK;

namespace LazerSR.Hook.Widgets;

/// <summary>
/// 키뷰어. 하단의 키 박스가 눌림 상태를 보여주고, 그 바로 위에서 막대가 자라 위로 흘러간다.
///
/// <para>
/// osu! 본체의 <see cref="KeyCounterDisplay"/>를 그대로 상속한다. 그래서 키 개수 결정,
/// 리플레이·관전·실플레이 공통 입력 경로, 스킨 에디터 배치가 전부 공짜다 —
/// 리플레이 입력도 <c>RulesetInputManager.HandleInputStateChange</c>가
/// <c>KeyBindingContainer.TriggerPressed</c>로 돌려주므로 세 경우를 구분할 필요가 없다.
/// </para>
/// </summary>
public partial class KeyViewerWidget : KeyCounterDisplay
{
    [SettingSource("스크롤 속도 (px/초)")]
    public BindableNumber<float> ScrollSpeed { get; } =
        new BindableFloat(400) { MinValue = 50, MaxValue = 2000, Precision = 10 };

    [SettingSource("막대 영역 높이 px")]
    public BindableNumber<float> BarAreaHeight { get; } =
        new BindableFloat(320) { MinValue = 50, MaxValue = 1600, Precision = 10 };

    [SettingSource("키 박스 크기 px")]
    public BindableNumber<float> KeySize { get; } =
        new BindableFloat(44) { MinValue = 16, MaxValue = 120, Precision = 1 };

    [SettingSource("키 간격 px")]
    public BindableNumber<float> KeySpacing { get; } =
        new BindableFloat(4) { MinValue = 0, MaxValue = 40, Precision = 1 };

    /// <summary>키 박스 테두리와 키 번호 글자색.</summary>
    [SettingSource("테두리 / 번호 색")]
    public BindableColour4 OutlineColour { get; } = new BindableColour4(Colour4.White);

    /// <summary>눌렸을 때 키 박스를 채우는 색이자 자라나는 막대의 색.</summary>
    [SettingSource("눌림 / 막대 색")]
    public BindableColour4 ActiveColour { get; } = new BindableColour4(Colour4.White);

    [SettingSource("아래로 흐르기", "키 박스가 위로 가고 막대가 아래로 자란다")]
    public BindableBool Downward { get; } = new BindableBool();

    [SettingSource("죽은 클릭 표시", "노트 판정을 일으키지 않은 입력을 테두리 색으로 구분한다")]
    public BindableBool ShowDeadPresses { get; } = new BindableBool();

    protected override FillFlowContainer<KeyCounter> KeyFlow { get; }

    // 이 프로젝트의 다른 위젯들이 검증한 경로다. ScoreProcessor를 직접 resolve하지 않는다.
    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    /// <summary>컬럼별 누적 판정 수. 어떤 입력이 실제로 노트를 건드렸는지 되짚는 데 쓴다.</summary>
    private readonly Dictionary<int, int> judgementCounts = new Dictionary<int, int>();

    public KeyViewerWidget()
    {
        Child = KeyFlow = new FillFlowContainer<KeyCounter>
        {
            Direction = FillDirection.Horizontal,
            AutoSizeAxes = Axes.Both,
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // 기본 동작은 "리플레이일 때만 강제 표시 + 그 외엔 키 오버레이 설정을 따름"이다.
        // 스킨에 직접 배치한 위젯이므로 항상 보이는 쪽이 맞다.
        AlwaysVisible.UnbindBindings();
        AlwaysVisible.Value = true;

        KeySpacing.BindValueChanged(e => KeyFlow.Spacing = new Vector2(e.NewValue, 0), true);

        if (gameplayState != null)
            gameplayState.ScoreProcessor.NewJudgement += onNewJudgement;
    }

    /// <summary>
    /// 판정이 나면 그 컬럼의 카운터를 올린다.
    ///
    /// <para>
    /// 시각을 대조하지 않고 <b>카운터 증가</b>로 판단하는 이유: 입력 시각은 HUD의 게임플레이 시계로,
    /// <c>JudgementResult.TimeAbsolute</c>는 룰셋의 프레임 안정 시계로 재어진다. 두 시계는 최대 한 스텝
    /// 어긋나 있어서 시각 대조는 신뢰할 수 없다. 카운터는 시계에 전혀 의존하지 않는다.
    /// </para>
    /// </summary>
    private void onNewJudgement(JudgementResult result)
    {
        if (result.HitObject is not ManiaHitObject maniaObject)
            return;

        judgementCounts.TryGetValue(maniaObject.Column, out int count);
        judgementCounts[maniaObject.Column] = count + 1;
    }

    /// <summary>그 컬럼에서 지금까지 발생한 판정 수.</summary>
    internal int JudgementCount(int column)
    {
        judgementCounts.TryGetValue(column, out int count);
        return count;
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (gameplayState != null)
            gameplayState.ScoreProcessor.NewJudgement -= onNewJudgement;
    }

    /// <summary>
    /// 번호는 배치 순서를 그대로 따른다 — 가장 왼쪽이 1번.
    /// <c>KeyCounterDisplay</c>가 <c>KeyFlow.Add(CreateCounter(trigger))</c> 형태로 부르므로,
    /// 이 시점의 <c>KeyFlow.Count</c>는 이미 들어간 개수와 같다.
    /// </summary>
    protected override KeyCounter CreateCounter(InputTrigger trigger)
    {
        // 컬럼 번호는 트리거의 액션에서 정확히 얻는다 (mania는 액션 번호 = 컬럼 번호).
        int column = trigger is KeyCounterActionTrigger<ManiaAction> actionTrigger ? (int)actionTrigger.Action : KeyFlow.Count;

        return new KeyViewerKey(this, trigger, KeyFlow.Count + 1, column);
    }
}
