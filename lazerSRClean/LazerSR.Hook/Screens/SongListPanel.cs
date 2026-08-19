using System;
using System.Collections.Generic;
using LazerSR.Hook.Training;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Colour;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;

namespace LazerSR.Hook.Screens;

/// <summary>
/// 대기 화면 우측, 설정 패널과 시작 버튼 사이의 등록곡 목록.
/// 곡 등록 자체는 선곡 화면의 <c>TrainingStatusWidget</c>이 하고, 여기서는 켜고/끄고/듣고/지운다.
/// <para>
/// 목록은 <see cref="TrainingMusicStore.Changed"/>가 올 때마다 <b>행을 통째로 재생성</b>한다 —
/// 항목이 수십 개라 비용이 없고, 부분 갱신 상태를 들고 다니지 않아도 된다
/// (<c>PatternListPanel</c>의 접기/펼치기와 같은 방침).
/// </para>
/// </summary>
public partial class SongListPanel : CompositeDrawable
{
    private const float row_height = 34;
    private const float row_spacing = 4;

    private FillFlowContainer rows = null!;
    private OsuSpriteText emptyText = null!;

    /// <summary>미리듣기 채널. 게임플레이와 무관한 대기 화면 전용이라 그냥 하나만 들고 돌려 쓴다.</summary>
    private DrawableTrack? preview;

    private string? previewingHash;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private BeatmapManager? beatmaps { get; set; }

    public SongListPanel()
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
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 12, Top = 10, Bottom = 10 },
                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, 26),
                        new Dimension(),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Font = OsuFont.TorusAlternate.With(size: 18),
                                Text = "배경음",
                            },
                        },
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Children = new Drawable[]
                                {
                                    emptyText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.TopLeft,
                                        Origin = Anchor.TopLeft,
                                        Font = OsuFont.Default.With(size: 13),
                                        Colour = colourProvider.Content2,
                                        Text = "선곡 화면의 곡 추가 위젯으로 등록한다",
                                    },
                                    new OsuScrollContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        ScrollbarOverlapsContent = false,
                                        Child = rows = new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, row_spacing),
                                        },
                                    },
                                },
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

        TrainingMusicStore.Changed += syncRows;
        syncRows();
    }

    /// <summary>
    /// 곡 <b>구성이 바뀐 경우에만</b> 행을 다시 만든다. 체크박스 토글처럼 행 안에서 끝나는 변화로는
    /// 재생성하지 않는다 — 재생성은 배경 이미지 재로딩과 깜빡임을 부른다.
    /// </summary>
    private void syncRows()
    {
        var songs = TrainingMusicStore.Songs;

        emptyText.Alpha = songs.Count == 0 ? 1 : 0;

        if (rowsMatch(songs))
            return;

        rows.Clear();

        foreach (var song in songs)
        {
            var beatmapInfo = beatmaps == null ? null : MusicLibrary.Resolve(beatmaps, song);

            rows.Add(new SongRow(song, beatmapInfo, togglePreview));
        }

        updatePreviewLabels();
    }

    private bool rowsMatch(IReadOnlyList<MusicTrackInfo> songs)
    {
        var existing = rows.Children;

        if (existing.Count != songs.Count)
            return false;

        for (int i = 0; i < songs.Count; i++)
        {
            if (existing[i] is not SongRow row || row.Hash != songs[i].AudioHash)
                return false;
        }

        return true;
    }

    /// <summary>재생 중 표시는 버튼 글자 하나뿐이라 행을 다시 만들 필요가 없다.</summary>
    private void updatePreviewLabels()
    {
        foreach (var child in rows.Children)
        {
            if (child is SongRow row)
                row.SetPreviewing(row.Hash == previewingHash);
        }
    }

    /// <summary>같은 곡을 다시 누르면 멈추고, 다른 곡을 누르면 그 곡으로 갈아탄다.</summary>
    private void togglePreview(MusicTrackInfo song)
    {
        bool wasPlaying = previewingHash == song.AudioHash;

        stopPreview();

        if (wasPlaying || beatmaps == null)
        {
            updatePreviewLabels();
            return;
        }

        var track = MusicLibrary.LoadTrack(beatmaps, song);

        if (track == null)
        {
            // 맵이 지워졌다. 등록은 참조일 뿐이므로 목록에는 남겨두고 재생만 포기한다.
            HookLog.Write($"[LazerSR] SongListPanel preview failed for {song.Title}.");
            updatePreviewLabels();
            return;
        }

        var drawable = new DrawableTrack(track);

        // 드로어블이 로드되기 전에는 seek/start가 먹지 않는다 (TrainingMusicPlayer와 같은 가드).
        // 로드가 끝나기 전에 사용자가 껐을 수 있으므로 그 사이에 바뀌지 않았는지 확인한다.
        drawable.OnLoadComplete += _ =>
        {
            if (preview != drawable)
                return;

            // 실제로 훈련에서 곡이 시작되는 지점 그대로 들려준다.
            drawable.Seek(MusicLibrary.SnappedStartMs(song));
            drawable.Start();
        };

        preview = drawable;
        previewingHash = song.AudioHash;

        AddInternal(drawable);

        updatePreviewLabels();
    }

    private void stopPreview()
    {
        previewingHash = null;

        if (preview == null)
            return;

        preview.Stop();
        RemoveInternal(preview, true);
        preview = null;
    }

    protected override void Dispose(bool isDisposing)
    {
        // static 이벤트에 델리게이트가 남으면 이 패널이 GC되지 않는다.
        TrainingMusicStore.Changed -= syncRows;

        base.Dispose(isDisposing);
    }

    /// <summary>곡 한 줄 — 체크박스 / 미리듣기 / 난이도 이름 / 여유 길이 / bpm / 삭제.</summary>
    private partial class SongRow : CompositeDrawable
    {
        private const float checkbox_width = 42;
        private const float preview_width = 30;
        private const float spare_width = 52;
        private const float bpm_width = 74;
        private const float delete_width = 30;

        private readonly MusicTrackInfo song;
        private readonly BeatmapInfo? beatmapInfo;
        private readonly Action<MusicTrackInfo> onPreview;

        private StepButton? previewButton;
        private bool previewing;

        /// <summary>이 행이 어떤 곡인지. 목록 동기화가 이걸로 구성 변화를 판단한다.</summary>
        public string Hash => song.AudioHash;

        /// <summary>
        /// <b>반드시 필드로 들고 있어야 한다.</b> <c>Bindable.BindTo</c>는 약한 참조라
        /// 지역 변수로 두면 GC가 수거하는 순간 체크박스 연결이 조용히 끊긴다
        /// (<c>PatternListPanel</c>이 실제로 이 함정에 걸렸다).
        /// </summary>
        private readonly BindableBool enabled = new BindableBool();

        public SongRow(MusicTrackInfo song, BeatmapInfo? beatmapInfo, Action<MusicTrackInfo> onPreview)
        {
            this.song = song;
            this.beatmapInfo = beatmapInfo;
            this.onPreview = onPreview;

            RelativeSizeAxes = Axes.X;
            Height = row_height;
            Masking = true;
            CornerRadius = 5;
        }

        /// <summary>행이 추가된 직후에도 불릴 수 있다 — 그때는 아직 BDL 전이라 버튼이 없으므로 값만 기억한다.</summary>
        public void SetPreviewing(bool value)
        {
            previewing = value;

            if (previewButton != null)
                previewButton.Label = previewLabel;
        }

        private string previewLabel => previewing ? "■" : "▶";

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            enabled.Value = song.Enabled;
            enabled.BindValueChanged(e => TrainingMusicStore.SetEnabled(song, e.NewValue));

            InternalChildren = new Drawable[]
            {
                // 이미지가 없을 때 비치는 바닥색.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background5,
                },
                // 바깥 컨테이너에 FillMode를 주면 안 된다 — RelativeSizeAxes.Both와 만나면 컨테이너가
                // 정사각형으로 부풀어, 행에는 그 정사각형의 윗부분만 보인다. 크롭 기준을 잡는 것은
                // 안쪽 스프라이트이고 osu!가 이미 Anchor/Origin = Centre + FillMode.Fill로 만들어 준다.
                new UpdateableBeatmapBackgroundSprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Beatmap = { Value = beatmapInfo },
                },
                // 글자가 읽히도록 덮는다 — 멀티 플레이리스트 행(DrawableRoomPlaylistItem)과 같은 방식이다.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(
                        Color4.Black.Opacity(0.75f),
                        Color4.Black.Opacity(0.45f)),
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 8 },
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, checkbox_width),
                        new Dimension(GridSizeMode.Absolute, preview_width),
                        new Dimension(),
                        new Dimension(GridSizeMode.Absolute, spare_width),
                        new Dimension(GridSizeMode.Absolute, bpm_width),
                        new Dimension(GridSizeMode.Absolute, delete_width),
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
                                Child = previewButton = new StepButton(previewLabel, () => onPreview(song))
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Masking = true,
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.Default.With(size: 14),
                                    Text = string.IsNullOrEmpty(song.DifficultyName) ? song.Title : song.DifficultyName,
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.Default.With(size: 13, fixedWidth: true),
                                    Colour = colourProvider.Content2,
                                    Text = $"{song.SpareMs / 1000:0}s",
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.Default.With(size: 13, fixedWidth: true),
                                    Colour = colourProvider.Content2,
                                    // 원본 bpm이 아니라 사용자가 정한 펄스 bpm이다 — 배속이 이 값에서 나온다.
                                    Text = $"{song.BaseBpm:0}bpm",
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new StepButton("×", () => TrainingMusicStore.Remove(song))
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
    }
}
