using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osuTK.Graphics;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 대기 화면의 −/+ 스테퍼 버튼. 패턴 목록의 bpm 조절과 설정 패널의 목표 정확도 조절이 같이 쓴다.
/// 값이 얼마나 움직이는지는 호출부가 정한다 — 이 버튼은 클릭만 전달한다.
/// </summary>
internal partial class StepButton : OsuClickableContainer
{
    private string label;
    private OsuSpriteText labelText = null!;

    /// <summary>버튼 하나가 상태에 따라 다른 글자를 보여줘야 할 때 쓴다 (미리듣기 ▶/■).</summary>
    public string Label
    {
        get => label;
        set
        {
            label = value;

            if (labelText.IsNotNull())
                labelText.Text = value;
        }
    }

    public StepButton(string label, Action action)
    {
        this.label = label;

        Action = action;
        Size = new osuTK.Vector2(24, 22);
        Masking = true;
        CornerRadius = 4;
    }

    /// <remarks>
    /// <c>OverlayColourProvider</c>는 대기 화면과 선곡 화면에는 있지만 <b>게임플레이 HUD에는 없다</b> —
    /// 같은 위젯이 두 곳에서 다 로드되므로 없어도 죽지 않아야 한다.
    /// </remarks>
    [BackgroundDependencyLoader(true)]
    private void load(OverlayColourProvider? colourProvider)
    {
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colourProvider?.Background3 ?? new Color4(50, 50, 60, 255),
            },
            labelText = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = OsuFont.Default.With(size: 15, weight: FontWeight.Bold),
                Text = label,
            },
        };
    }
}
