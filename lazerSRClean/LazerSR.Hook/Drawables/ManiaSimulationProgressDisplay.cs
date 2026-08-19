using System;
using LazerSR.Hook.Data;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Drawables;

/// <summary>
/// 로딩 화면에서 판정 분석 진행률을 보여주는 막대.
/// 시뮬레이션이 끝날 때까지 로딩이 대기하므로, 얼마나 남았는지 보여줄 필요가 있다.
///
/// <para>
/// 크기를 고정한 컨테이너 안에서만 중앙 정렬을 쓴다 — AutoSize 컨테이너에 Centre 앵커를 섞는 조합은
/// 과거에 프로세스가 즉사한 전례가 있다 (`ui-patching.md` 함정 표).
/// </para>
/// </summary>
internal partial class ManiaSimulationProgressDisplay : CompositeDrawable
{
    private const float bar_height = 6f;

    private readonly ManiaSimulationResult target;

    private OsuSpriteText label = null!;
    private Box fill = null!;

    private bool dismissed;

    public ManiaSimulationProgressDisplay(ManiaSimulationResult target)
    {
        this.target = target;

        Anchor = Anchor.BottomCentre;
        Origin = Anchor.BottomCentre;
        Size = new Vector2(360, 44);
        Y = -110;

        // 로딩 화면의 준비 판정(ReadyForGameplay)이 hover 상태를 보므로 입력에 절대 관여하지 않는다.
        AlwaysPresent = true;
    }

    protected override bool ShouldBeConsideredForInput(Drawable child) => false;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            label = new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Font = OsuFont.Torus.With(size: 15, weight: FontWeight.SemiBold),
                Colour = Color4.White,
                Text = "판정 분석 중",
            },
            new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                RelativeSizeAxes = Axes.X,
                Height = bar_height,
                Masking = true,
                CornerRadius = bar_height / 2,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(255, 255, 255, 60),
                    },
                    fill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0f,
                        Colour = Color4.White,
                    },
                },
            },
        };
    }

    protected override void Update()
    {
        base.Update();

        if (dismissed)
            return;

        if (target.Ready)
        {
            dismissed = true;
            this.FadeOut(200, Easing.Out);
            return;
        }

        float progress = (float)Math.Clamp(target.Progress, 0, 1);

        fill.Width = progress;
        label.Text = $"판정 분석 중  {progress:P0}";
    }
}
