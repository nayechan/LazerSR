using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;

namespace LazerSR.Hook.Widgets;

public class ManiaPositionAdjustWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(canBeNull: true)]
    private SkinManager? skinManager { get; set; }

    private SquareCheckbox checkbox = null!;
    private FillFlowContainer body = null!;
    private PositionField hitField = null!;
    private PositionField scoreField = null!;
    private bool expanded = true;

    public ManiaPositionAdjustWidget()
    {
        AutoSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 6;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.Black,
                Alpha = 0.55f,
            },
            new Container
            {
                AutoSizeAxes = Axes.Both,
                Padding = new MarginPadding(8),
                Child = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        checkbox = new SquareCheckbox(toggleExpanded)
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                        },
                        body = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                hitField = new PositionField("HitPosition", d => adjustHitPosition(d)),
                                scoreField = new PositionField("ScorePosition", d => adjustScorePosition(d)),
                            },
                        },
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        refreshDisplay();
    }

    private void toggleExpanded()
    {
        expanded = !expanded;
        checkbox.SetChecked(expanded);

        // Only Alpha changes — the widget's own AutoSize footprint never shrinks, so the
        // checkbox (and the widget's placed position/anchor in the skin) never shifts.
        body.Alpha = expanded ? 1f : 0f;
    }

    private void adjustHitPosition(float delta)
    {
        var config = currentConfig();
        if (config == null) return;

        config.HitPosition += delta;
        hitField.SetValue(config.HitPosition);
        notifySkinChanged();
    }

    private void adjustScorePosition(float delta)
    {
        var config = currentConfig();
        if (config == null) return;

        config.ScorePosition += delta;
        scoreField.SetValue(config.ScorePosition);
        notifySkinChanged();
    }

    private void refreshDisplay()
    {
        var config = currentConfig();
        if (config == null) return;

        hitField.SetValue(config.HitPosition);
        scoreField.SetValue(config.ScorePosition);
    }

    private LegacyManiaSkinConfiguration? currentConfig()
    {
        if (gameplayState?.Beatmap is not ManiaBeatmap maniaBeatmap) return null;
        if (skinManager?.CurrentSkin.Value is not Skin skin) return null;

        return ManiaSkinPositionAccess.GetOrCreateConfiguration(skin, maniaBeatmap.TotalColumns);
    }

    private void notifySkinChanged()
    {
        if (skinManager != null)
            ManiaSkinPositionAccess.NotifySkinChanged(skinManager);
    }

    private sealed partial class PositionField : FillFlowContainer
    {
        private readonly OsuSpriteText valueText;

        public PositionField(string label, Action<float> onAdjust)
        {
            AutoSizeAxes = Axes.Both;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 2);

            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = label,
                    Font = OsuFont.Default.With(size: 13, weight: FontWeight.SemiBold),
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new StepButton("-", () => onAdjust(-1f)),
                        // Fix (2026-07-18): the value text must NOT sit directly in the
                        // FillFlowContainer with Anchor.Centre + a partially-fixed size
                        // (fixed Width, auto Height) — that combination, nested several
                        // AutoSizeAxes.Both FillFlowContainers deep, crashed osu! outright
                        // (confirmed via bisection). Wrapping it in a fully fixed-size
                        // Container and centering the text inside THAT avoids the mixed
                        // fixed/auto sizing that triggered it.
                        new Container
                        {
                            Size = new Vector2(44, 20),
                            Child = valueText = new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Font = OsuFont.Default.With(size: 16, weight: FontWeight.Bold),
                                Text = "—",
                            },
                        },
                        new StepButton("+", () => onAdjust(1f)),
                    },
                },
            };
        }

        public void SetValue(float value) => valueText.Text = value.ToString("F0");
    }

    private sealed partial class StepButton : OsuClickableContainer
    {
        public StepButton(string symbol, Action action)
        {
            Size = new Vector2(20, 20);
            Action = action;
            Masking = true;
            CornerRadius = 3;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Colour4.FromHex("#3a3a3a"),
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = symbol,
                    Font = OsuFont.Default.With(size: 14, weight: FontWeight.Bold),
                },
            };
        }
    }

    private sealed partial class SquareCheckbox : OsuClickableContainer
    {
        private readonly Box fill;

        public SquareCheckbox(Action toggle)
        {
            Size = new Vector2(11, 11);
            Action = toggle;
            Masking = true;
            CornerRadius = 3;
            BorderThickness = 1.5f;
            BorderColour = Colour4.White;

            Child = fill = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.White,
                Alpha = 1f,
            };
        }

        public void SetChecked(bool isChecked) => fill.Alpha = isChecked ? 1f : 0.3f;
    }
}
