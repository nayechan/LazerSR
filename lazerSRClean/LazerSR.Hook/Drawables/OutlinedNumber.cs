using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Drawables;

/// <summary>
/// 검은 테두리를 두른 숫자. 노트·사각형 위에 겹쳐도 읽히게 하는 것이 목적이다.
///
/// <para>
/// osu.Framework의 <see cref="SpriteText.Shadow"/>는 한쪽 방향 그림자뿐이라 테두리가 되지 않는다.
/// 그래서 같은 글자를 8방향으로 조금씩 밀어 검게 깔고 그 위에 본 색을 얹는다.
/// </para>
/// </summary>
internal partial class OutlinedNumber : CompositeDrawable
{
    private static readonly Vector2[] offsets =
    {
        new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1),
        new Vector2(-1, 0), new Vector2(1, 0),
        new Vector2(-1, 1), new Vector2(0, 1), new Vector2(1, 1),
    };

    private readonly OsuSpriteText[] outline = new OsuSpriteText[offsets.Length];
    private readonly OsuSpriteText main;

    private string text = string.Empty;

    public OutlinedNumber(float size, float thickness)
    {
        AutoSizeAxes = Axes.Both;

        var font = OsuFont.Torus.With(size: size, weight: FontWeight.Bold);
        var children = new Drawable[offsets.Length + 1];

        for (int i = 0; i < offsets.Length; i++)
        {
            children[i] = outline[i] = new OsuSpriteText
            {
                Font = font,
                Colour = Color4.Black,
                // 자식은 전부 TopLeft 기준으로 둔다 — AutoSize 컨테이너에 Centre 앵커를 섞지 않기 위함.
                Position = offsets[i] * thickness + new Vector2(thickness),
            };
        }

        children[offsets.Length] = main = new OsuSpriteText
        {
            Font = font,
            Position = new Vector2(thickness),
        };

        InternalChildren = children;
    }

    public string Text
    {
        set
        {
            if (text == value)
                return;

            text = value;
            main.Text = value;

            foreach (var sprite in outline)
                sprite.Text = value;
        }
    }

    /// <summary>글자 색. 테두리는 항상 검정이므로 <see cref="Drawable.Colour"/>가 아니라 이걸 쓴다.</summary>
    public Color4 TextColour
    {
        set => main.Colour = value;
    }
}
