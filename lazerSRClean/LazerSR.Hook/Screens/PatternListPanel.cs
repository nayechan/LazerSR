using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LazerSR.Hook;
using LazerSR.Hook.Training;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 좌측 패턴 목록 패널. 대분류를 펼치면 세부패턴 행이 나오고, 행마다 체크박스·bpm 스테퍼·sunnySR pill이 붙는다.
/// <para>
/// 체크박스와 bpm은 <see cref="TrainingProfile"/>이 들고 있으므로 행을 다시 만들어도, 화면을 나갔다
/// 들어와도 유지된다(세션 한정). <b>단기 bpm은 측정 시작값이자 결과값</b>이라 여기서 조정한 값이
/// 그대로 탐색 시작점이 된다.
/// </para>
/// <para>
/// 접기/펼치기는 중첩 컨테이너를 토글하지 않고 <b>행 목록을 통째로 다시 만든다</b>. 항목이 수십 개 수준이라
/// 비용이 없고, <c>AutoSizeAxes</c> 중첩 체인을 만들지 않아 레이아웃 크래시 함정
/// (<c>docs\guides\ui-patching.md</c> §6)을 원천적으로 피한다.
/// </para>
/// </summary>
public partial class PatternListPanel : CompositeDrawable
{
    private const float header_height = 44;
    private const float group_row_height = 38;
    private const float sub_row_height = 34;

    private readonly HashSet<string> expandedGroups = new HashSet<string>();

    private FillFlowContainer rows = null!;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    public PatternListPanel()
    {
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 10;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colourProvider.Background4,
            },
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.Absolute, header_height),
                    new Dimension(),
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colourProvider.Background3,
                                },
                                new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Margin = new MarginPadding { Left = 16 },
                                    Font = OsuFont.TorusAlternate.With(size: 20),
                                    Text = "패턴 목록",
                                },
                            },
                        },
                    },
                    new Drawable[]
                    {
                        new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollbarVisible = true,
                            Child = rows = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new osuTK.Vector2(0, 2),
                                Padding = new MarginPadding { Vertical = 6, Horizontal = 6 },
                            },
                        },
                    },
                },
            },
        };

        rebuildRows();
    }

    private void rebuildRows()
    {
        rows.Clear();

        foreach (var group in PatternCatalog.Groups)
        {
            bool expanded = expandedGroups.Contains(group.Id);

            rows.Add(new GroupHeaderRow(group, expanded, () => toggleGroup(group.Id))
            {
                Height = group_row_height,
            });

            if (!expanded)
                continue;

            if (group.SubPatterns.Count == 0)
            {
                rows.Add(new EmptyGroupRow { Height = sub_row_height });
                continue;
            }

            foreach (var subPattern in group.SubPatterns)
                rows.Add(new SubPatternRow(subPattern) { Height = sub_row_height });
        }
    }

    private void toggleGroup(string groupId)
    {
        if (!expandedGroups.Remove(groupId))
            expandedGroups.Add(groupId);

        rebuildRows();
    }

    /// <summary>대분류 헤더. 클릭하면 접기/펼치기.</summary>
    private partial class GroupHeaderRow : CompositeDrawable
    {
        private readonly PatternGroup group;
        private readonly bool expanded;
        private readonly Action onClick;

        public GroupHeaderRow(PatternGroup group, bool expanded, Action onClick)
        {
            this.group = group;
            this.expanded = expanded;
            this.onClick = onClick;

            RelativeSizeAxes = Axes.X;
            Masking = true;
            CornerRadius = 5;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            InternalChild = new OsuClickableContainer
            {
                RelativeSizeAxes = Axes.Both,
                Action = onClick,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background3,
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 12 },
                        Size = new osuTK.Vector2(10),
                        Icon = expanded ? FontAwesome.Solid.ChevronDown : FontAwesome.Solid.ChevronRight,
                        Colour = colourProvider.Content2,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 32 },
                        Font = OsuFont.Default.With(size: 17, weight: FontWeight.SemiBold),
                        Text = group.DisplayName,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = 14 },
                        Font = OsuFont.Default.With(size: 13),
                        Colour = colourProvider.Content2,
                        Text = group.SubPatterns.Count > 0 ? $"{group.SubPatterns.Count}개" : "미구현",
                    },
                },
            };
        }
    }

    /// <summary>세부패턴이 아직 없는 대분류를 펼쳤을 때 보여줄 자리.</summary>
    private partial class EmptyGroupRow : CompositeDrawable
    {
        public EmptyGroupRow()
        {
            RelativeSizeAxes = Axes.X;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            InternalChild = new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Margin = new MarginPadding { Left = 44 },
                Font = OsuFont.Default.With(size: 14),
                Colour = colourProvider.Foreground1,
                Text = "생성 규칙이 아직 없습니다.",
            };
        }
    }

    /// <summary>세부패턴 행. 체크박스 / 이름 / 단기 bpm 스테퍼 / sunnySR pill.</summary>
    private partial class SubPatternRow : CompositeDrawable
    {
        private const float checkbox_width = 46;
        private const float stepper_width = 116;
        private const float pill_width = 76;

        private readonly SubPattern subPattern;

        /// <summary>
        /// <b>반드시 필드로 들고 있어야 한다.</b> osu.Framework의 <c>Bindable.BindTo</c>는 약한 참조로 묶여서,
        /// 지역 변수로 두면 GC가 수거하는 순간 체크박스와의 연결이 조용히 끊긴다
        /// (bpm 스테퍼가 합성 보면 계산으로 GC를 유발하면 그 직후 체크박스가 먹통이 됐다).
        /// </summary>
        private readonly osu.Framework.Bindables.BindableBool enabled = new osu.Framework.Bindables.BindableBool();

        private OsuSpriteText bpmText = null!;
        private StarRatingDisplay pill = null!;

        private CancellationTokenSource? starRatingCts;

        public SubPatternRow(SubPattern subPattern)
        {
            this.subPattern = subPattern;

            RelativeSizeAxes = Axes.X;
            Masking = true;
            CornerRadius = 5;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            enabled.Value = TrainingProfile.IsEnabled(subPattern);
            enabled.BindValueChanged(e => TrainingProfile.SetEnabled(subPattern, e.NewValue));

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background5,
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 10 },
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, checkbox_width),
                        new Dimension(),
                        new Dimension(GridSizeMode.Absolute, stepper_width),
                        new Dimension(GridSizeMode.Absolute, pill_width),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new OsuCheckbox(nubOnRight: false)
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Current = { BindTarget = enabled },
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.Default.With(size: 15),
                                    Text = subPattern.DisplayName,
                                },
                            },
                            createStepper(colourProvider),
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = pill = new StarRatingDisplay(default, StarRatingDisplaySize.Small)
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                },
                            },
                        },
                    },
                },
            };
        }

        private Drawable createStepper(OverlayColourProvider colourProvider) => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new StepButton("−", () => step(-1))
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                },
                bpmText = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 30 },
                    Font = OsuFont.Default.With(size: 14, fixedWidth: true),
                },
                new StepButton("+", () => step(1))
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 86 },
                },
            },
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            updateBpmText();
            requestStarRating();
        }

        private void step(int direction)
        {
            TrainingProfile.StepShortTermBpm(subPattern, direction);

            updateBpmText();
            requestStarRating();
        }

        private void updateBpmText() => bpmText.Text = $"{TrainingProfile.GetShortTermBpm(subPattern):0}";

        /// <summary>행마다 <b>독립적인</b> 합성 보면을 만들어 계산한다. 값이 매번 새로 뽑히므로 캐시하지 않는다.</summary>
        private void requestStarRating()
        {
            starRatingCts?.Cancel();
            starRatingCts = new CancellationTokenSource();

            var token = starRatingCts.Token;
            double bpm = TrainingProfile.GetShortTermBpm(subPattern);
            var factory = subPattern.CreateGenerator;

            pill.Alpha = 0.4f;

            Task.Run(() =>
            {
                try
                {
                    double sr = SyntheticPatternMap.CalculateSunnySr(factory(), bpm, token);

                    if (token.IsCancellationRequested)
                        return;

                    Schedule(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        pill.Current.Value = new StarDifficulty(sr, 0);
                        pill.Alpha = 1;
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    HookLog.Write($"[LazerSR] PatternListPanel star rating failed: {e}");
                }
            }, token);
        }

        protected override void Dispose(bool isDisposing)
        {
            starRatingCts?.Cancel();

            base.Dispose(isDisposing);
        }
    }
}
