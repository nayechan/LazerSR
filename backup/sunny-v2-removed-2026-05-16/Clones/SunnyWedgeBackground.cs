// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Adapted from osu.Game/Screens/Select/WedgeBackground.cs for LazerSR sunny display (Sibling-Clone strategy).
// Original is `internal sealed` and cannot be referenced from this assembly, so a public clone lives here.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Overlays;

namespace LazerSR.Hook.Clones;

public sealed partial class SunnyWedgeBackground : InputBlockingContainer
{
    public float StartAlpha { get; init; } = 0.9f;

    public float FinalAlpha { get; init; } = 0.6f;

    public float WidthForGradient { get; init; } = 0.3f;

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                Blending = BlendingParameters.Additive,
                RelativeSizeAxes = Axes.Both,
                Width = 0.6f,
                Alpha = 0.5f,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background2, colourProvider.Background2.Opacity(0)),
            },
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Width = 1 - WidthForGradient,
                Colour = colourProvider.Background5.Opacity(StartAlpha),
            },
            new Box
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                RelativeSizeAxes = Axes.Both,
                Width = WidthForGradient,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background5.Opacity(StartAlpha), colourProvider.Background5.Opacity(FinalAlpha)),
            },
        };
    }
}
