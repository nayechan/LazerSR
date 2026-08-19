using LazerSR.Hook.Data;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;

namespace LazerSR.Hook.Drawables;

internal sealed class MsdBarChart : CompositeDrawable
{
    private const float MaxVal = 40f;
    private const float LabelH = 14f;
    private const float BarGap = 3f;
    private const float CornerRad = 2f;

    private static readonly Colour4[] Colors =
    {
        new Colour4(0.69f, 0.69f, 0.69f, 1f), // Overall
        new Colour4(0.36f, 0.78f, 0.96f, 1f), // Stream
        new Colour4(0.44f, 0.87f, 0.48f, 1f), // Jumpstream
        new Colour4(0.96f, 0.66f, 0.31f, 1f), // Handstream
        new Colour4(0.76f, 0.45f, 0.94f, 1f), // Stamina
        new Colour4(0.94f, 0.39f, 0.39f, 1f), // Jackspeed
        new Colour4(0.96f, 0.83f, 0.31f, 1f), // Chordjack
        new Colour4(0.31f, 0.96f, 0.83f, 1f), // Technical
    };

    private static readonly string[] SkillLabels = { "OVR", "STR", "JS", "HS", "STA", "JK", "CJ", "TEC" };

    private readonly Container[] bars = new Container[8];
    private readonly OsuSpriteText[] labels = new OsuSpriteText[8];
    private float[] _values = new float[8];

    public MsdBarChart()
    {
        for (int i = 0; i < 8; i++)
        {
            AddInternal(bars[i] = new Container
            {
                Masking = true,
                CornerRadius = CornerRad,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Colors[i] },
            });
            AddInternal(labels[i] = new OsuSpriteText
            {
                Text = SkillLabels[i],
                Font = OsuFont.Default.With(size: 10f),
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Colour = Colour4.White.Opacity(0.7f),
            });
        }
    }

    public void SetData(MsdData data)
    {
        _values = new float[]
        {
            data.Overall, data.Stream, data.Jumpstream, data.Handstream,
            data.Stamina, data.Jackspeed, data.Chordjack, data.Technical,
        };
    }

    protected override void Update()
    {
        base.Update();

        float w = DrawWidth;
        float h = DrawHeight;
        if (w <= 0 || h <= 0) return;

        float barAreaH = h - LabelH;
        float colW = (w - BarGap * 9) / 8f;

        for (int i = 0; i < 8; i++)
        {
            float x = BarGap + i * (colW + BarGap);
            float ratio = Math.Clamp(_values[i] / MaxVal, 0f, 1f);
            float barH = Math.Max(barAreaH * ratio, 1f);

            bars[i].X = x;
            bars[i].Y = barAreaH - barH;
            bars[i].Width = colW;
            bars[i].Height = barH;

            labels[i].X = x + colW / 2f;
            labels[i].Y = h - LabelH / 2f;
        }
    }
}
