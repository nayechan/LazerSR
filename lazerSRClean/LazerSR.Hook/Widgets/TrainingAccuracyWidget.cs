using LazerSR.Hook.Training;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

/// <summary>
/// 무한 트레이닝 전용 정확도. 분모 320, 분자 <c>[320,300,200,100,50,0]</c>의 단순 산술평균이다.
/// <para>
/// <b>계산은 <see cref="JudgementAggregator"/>가 전담하고 이 위젯은 표시만 한다.</b>
/// 화면의 숫자와 탐색 알고리즘의 판단 근거가 갈라질 여지를 없애기 위해서다.
/// </para>
/// <para>
/// 값은 <b>세트 단위로 끊긴다</b> — 세트가 바뀌면 집계가 리셋되고, 집계된 노트가 없는 동안에는 100%다.
/// </para>
/// </summary>
public partial class TrainingAccuracyWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [SettingSource("글꼴")]
    public Bindable<Typeface> Font { get; } = new Bindable<Typeface>(Typeface.Torus);

    private readonly IBindable<double> accuracy = TrainingSessionState.CurrentAccuracy.GetBoundCopy();

    private OsuSpriteText text = null!;

    public TrainingAccuracyWidget()
    {
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = text = new OsuSpriteText
        {
            Text = "100.00%",
            Font = OsuFont.GetFont(Font.Value, size: 24, weight: FontWeight.Bold),
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Font.BindValueChanged(e => text.Font = OsuFont.GetFont(e.NewValue, size: 24, weight: FontWeight.Bold), true);
        accuracy.BindValueChanged(e => text.Text = $"{e.NewValue * 100:0.00}%", true);
    }
}
