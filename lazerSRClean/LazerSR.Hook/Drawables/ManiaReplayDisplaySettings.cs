using LazerSR.Hook.Data;
using osu.Framework.Allocation;
using osu.Game.Screens.Play.PlayerSettings;

namespace LazerSR.Hook.Drawables;

/// <summary>
/// 리플레이 설정 창(오른쪽 패널)에 붙는 그룹. 우리가 추가한 표시 4종을 각각 켜고 끈다.
///
/// <para>
/// osu! 본체가 <c>ReplayPlayer.AddSettings(PlayerSettingsGroup)</c>를 public으로 열어두고 있고,
/// osu!std의 <c>ReplayAnalysisSettings</c>가 같은 방식으로 커서 분석 토글을 넣는다. 같은 경로를 쓴다.
/// </para>
/// </summary>
public partial class ManiaReplayDisplaySettings : PlayerSettingsGroup
{
    public ManiaReplayDisplaySettings()
        : base("판정 표시")
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        AddRange(new[]
        {
            new PlayerCheckbox
            {
                LabelText = "노트 판정선",
                Current = ManiaOverlayVisibility.NoteLines,
            },
            new PlayerCheckbox
            {
                LabelText = "판정 기준선",
                Current = ManiaOverlayVisibility.JudgementLine,
            },
            new PlayerCheckbox
            {
                LabelText = "입력 구간",
                Current = ManiaOverlayVisibility.PressBoxes,
            },
            new PlayerCheckbox
            {
                LabelText = "판정 오차",
                Current = ManiaOverlayVisibility.OffsetNumbers,
            },
            new PlayerCheckbox
            {
                LabelText = "노트 숨기기",
                Current = ManiaOverlayVisibility.HideNotes,
            },
        });
    }
}
