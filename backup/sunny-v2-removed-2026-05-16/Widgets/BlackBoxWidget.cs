using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

public class BlackBoxWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    public BlackBoxWidget()
    {
        Width = 200;
        Height = 100;
        InternalChild = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = Colour4.Black,
        };
    }
}
