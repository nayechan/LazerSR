using osuTK.Input;

namespace LazerSR.Hook.Input;

/// <summary>
/// Windows 가상키 코드 → <see cref="Key"/> 변환. 두 체계는 숫자가 일치하지 않는다.
/// <para>
/// <b>최소 집합만 다룬다</b> — 알파벳·숫자·넘패드 숫자·스페이스·일부 OEM 기호(<c>[ ] ; ' , . /</c>).
/// mania 키설정은 거의 전부 여기 들어가고, 표에 없는 키는 <c>null</c>을 돌려 조용히 무시된다
/// (모르는 키를 잘못 매핑하는 것보다 안전하다).
/// </para>
/// <para>
/// <b>Esc는 의도적으로 제외</b>했다. 대상 게임에서 Esc를 누를 때마다 osu!가 일시정지되면 안 된다.
/// </para>
/// <para>
/// 좌/우 수식키(Shift·Ctrl·Alt)와 넘패드 Enter는 <c>MakeCode</c>와 E0 플래그를 함께 봐야 갈리므로
/// 지금은 다루지 않는다. 필요해지면 그때 확장한다.
/// </para>
/// </summary>
internal static class RawKeyMap
{
    private const int vk_space = 0x20;
    private const int vk_0 = 0x30, vk_9 = 0x39;
    private const int vk_a = 0x41, vk_z = 0x5A;
    private const int vk_numpad0 = 0x60, vk_numpad9 = 0x69;
    private const int vk_oem_1 = 0xBA; // ;
    private const int vk_oem_comma = 0xBC; // ,
    private const int vk_oem_period = 0xBE; // .
    private const int vk_oem_2 = 0xBF; // /
    private const int vk_oem_4 = 0xDB; // [
    private const int vk_oem_6 = 0xDD; // ]
    private const int vk_oem_7 = 0xDE; // '

    /// <summary>매핑이 없으면 <c>null</c>.</summary>
    public static Key? FromVirtualKey(int virtualKey)
    {
        // 각 구간은 열거형 안에서 연속이다(구간끼리는 떨어져 있다) — 그래서 구간별로만 산술을 쓴다.
        if (virtualKey >= vk_a && virtualKey <= vk_z)
            return Key.A + (virtualKey - vk_a);

        if (virtualKey >= vk_0 && virtualKey <= vk_9)
            return Key.Number0 + (virtualKey - vk_0);

        if (virtualKey >= vk_numpad0 && virtualKey <= vk_numpad9)
            return Key.Keypad0 + (virtualKey - vk_numpad0);

        if (virtualKey == vk_space)
            return Key.Space;

        switch (virtualKey)
        {
            case vk_oem_1: return Key.Semicolon;
            case vk_oem_comma: return Key.Comma;
            case vk_oem_period: return Key.Period;
            case vk_oem_2: return Key.Slash;
            case vk_oem_4: return Key.BracketLeft;
            case vk_oem_6: return Key.BracketRight;
            case vk_oem_7: return Key.Quote;
        }

        return null;
    }
}
