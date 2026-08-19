using LazerSR.Hook.Data;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.UI.Scrolling;
using osuTK.Graphics;

namespace LazerSR.Hook.Drawables;

/// <summary>
/// mania 컬럼 하나의 노트를 감춘다. 우리가 그린 판정 표시만 남기고 보고 싶을 때 쓴다.
///
/// <para>
/// <b>`Alpha`가 아니라 `Colour`를 투명하게 만든다.</b> `Alpha = 0`은 `IsPresent`를 false로 만드는데,
/// 우리 오버레이들이 바로 그 컨테이너의 <see cref="ScrollingHitObjectContainer.PositionAtTime(double)"/>에
/// 위치 계산을 의존하고 있어서 업데이트가 끊기면 표시가 통째로 얼어붙을 수 있다.
/// <c>Colour</c>는 `IsPresent` 판정에 들어가지 않으므로 그리기만 사라지고 업데이트는 그대로 돈다.
/// </para>
/// </summary>
internal partial class ManiaNoteHider : CompositeDrawable
{
    private readonly ScrollingHitObjectContainer hitObjectContainer;

    // static bindable에 직접 구독하면 이 드로어블이 GC되지 않는다. 필드로 보관되는 사본을 거친다.
    private readonly BindableBool hideNotes = new BindableBool();

    private ColourInfo originalColour;

    public ManiaNoteHider(ScrollingHitObjectContainer hitObjectContainer)
    {
        this.hitObjectContainer = hitObjectContainer;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // 스킨이나 모드가 이미 색을 손댔을 수 있으므로 원래 값을 되돌릴 수 있게 기억해 둔다.
        originalColour = hitObjectContainer.Colour;

        hideNotes.BindTo(ManiaOverlayVisibility.HideNotes);
        hideNotes.BindValueChanged(e => hitObjectContainer.Colour = e.NewValue ? Color4.Transparent : originalColour, true);
    }
}
