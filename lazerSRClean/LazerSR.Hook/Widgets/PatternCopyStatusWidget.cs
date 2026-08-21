using System.Collections.Generic;
using LazerSR.Hook.PatternCopy;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

/// <summary>
/// 패턴 복제 전용 표시 — 위에 콤보, 아래에 정확도.
/// <para>
/// 정확도는 <b>lazer 클라이언트 기본 방식</b>이다(mania 최고 판정 305 기준). 무한 트레이닝의
/// <see cref="TrainingAccuracyWidget"/>이 쓰는 320 분모와는 다른 계산이다.
/// </para>
/// <para>
/// <b>osu!의 <c>ScoreProcessor.Accuracy</c>를 그대로 쓰지 않는다.</b> 그 값은 게임플레이 시작
/// 시점부터 누적되므로 시드 비트맵의 진입용 동시치기까지 섞이고, 캡처 세션이 바뀌어도 리셋되지 않는다.
/// 대신 판정 이벤트를 직접 집계하고 <see cref="PatternCopySessionState.SessionIndex"/>가 바뀌면 리셋한다 —
/// 무한 트레이닝 위젯이 세트마다 리셋하는 것과 같은 구조다.
/// </para>
/// </summary>
public partial class PatternCopyStatusWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [SettingSource("글꼴")]
    public Bindable<Typeface> Font { get; } = new Bindable<Typeface>(Typeface.Torus);

    [SettingSource("콤보 표시")]
    public BindableBool ShowCombo { get; } = new BindableBool(true);

    [SettingSource("정확도 표시")]
    public BindableBool ShowAccuracy { get; } = new BindableBool(true);

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    private OsuSpriteText comboText = null!;
    private OsuSpriteText accuracyText = null!;

    private readonly IBindable<int> sessionIndex = PatternCopySessionState.SessionIndex.GetBoundCopy();

    private int combo;
    private double baseScore;
    private double maxBaseScore;

    /// <summary>되돌림을 대칭으로 처리하기 위한 직전 상태. 콤보는 계산으로 복원할 수 없어 쌓아둔다.</summary>
    private readonly Stack<(int Combo, double Base, double MaxBase)> history = new();

    public PatternCopyStatusWidget()
    {
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                comboText = new OsuSpriteText { Text = "0x" },
                accuracyText = new OsuSpriteText { Text = "100.00%" },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Font.BindValueChanged(_ => applyFont(), true);
        ShowCombo.BindValueChanged(e => comboText.Alpha = e.NewValue ? 1 : 0, true);
        ShowAccuracy.BindValueChanged(e => accuracyText.Alpha = e.NewValue ? 1 : 0, true);

        // 캡처가 새로 시작되면 누적값을 버린다.
        sessionIndex.BindValueChanged(_ => resetSession(), false);

        if (gameplayState == null)
            return;   // 스킨 에디터 — 배치용 플레이스홀더만 보여준다.

        gameplayState.ScoreProcessor.NewJudgement += onNewJudgement;
        gameplayState.ScoreProcessor.JudgementReverted += onJudgementReverted;
    }

    private void applyFont()
    {
        comboText.Font = OsuFont.GetFont(Font.Value, size: 32, weight: FontWeight.Bold);
        accuracyText.Font = OsuFont.GetFont(Font.Value, size: 24, weight: FontWeight.Bold);
    }

    private void resetSession()
    {
        combo = 0;
        baseScore = 0;
        maxBaseScore = 0;
        history.Clear();
        updateDisplay();
    }

    /// <summary>
    /// 조건과 가중치를 osu! <c>ScoreProcessor</c>와 동일하게 맞춘다 — 최고 판정은 <c>MaxResult</c> 기준,
    /// 실제 획득은 <c>Type</c> 기준으로 각각 따진다. <c>GetBaseScoreForResult</c>는 순수 조회 함수이고
    /// mania 오버라이드가 305 체계를 준다(읽기만 하므로 라이브 인스턴스를 건드리지 않는다).
    /// </summary>
    private void onNewJudgement(JudgementResult result)
    {
        var processor = gameplayState!.ScoreProcessor;

        history.Push((combo, baseScore, maxBaseScore));

        if (result.Type.IncreasesCombo()) combo++;
        else if (result.Type.BreaksCombo()) combo = 0;

        if (result.Judgement.MaxResult.AffectsAccuracy())
            maxBaseScore += processor.GetBaseScoreForResult(result.Judgement.MaxResult);

        if (result.Type.AffectsAccuracy())
            baseScore += processor.GetBaseScoreForResult(result.Type);

        updateDisplay();
    }

    /// <summary>
    /// <see cref="onNewJudgement"/>와 정확히 대칭. 되감기·스킵에서 <c>Playfield</c>가 LIFO로 되돌리므로
    /// 직전 상태를 그대로 꺼내 쓰면 어긋나지 않는다 (<c>feature-development.md</c>의 하드 룰).
    /// </summary>
    private void onJudgementReverted(JudgementResult result)
    {
        if (history.Count == 0) return;

        (combo, baseScore, maxBaseScore) = history.Pop();
        updateDisplay();
    }

    private void updateDisplay()
    {
        comboText.Text = $"{combo}x";

        double accuracy = maxBaseScore > 0 ? baseScore / maxBaseScore : 1.0;
        accuracyText.Text = $"{accuracy * 100:0.00}%";
    }

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayState != null)
        {
            gameplayState.ScoreProcessor.NewJudgement -= onNewJudgement;
            gameplayState.ScoreProcessor.JudgementReverted -= onJudgementReverted;
        }

        base.Dispose(isDisposing);
    }
}
