using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using LazerSR.Hook.Screens;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osuTK.Graphics;

namespace LazerSR.Hook.Patches;

/// <summary>
/// 메인 메뉴 <b>"편집"</b> 서브메뉴(비트맵 에디터/스킨 에디터)에 '패턴 복제' 버튼을 추가한다.
/// osu! 원본 <see cref="MainMenuButton"/>를 그대로 인스턴스화하므로 호버 애니메이션/비트 싱크/
/// 사운드/확장·축소 상태머신이 다른 버튼과 완전히 동일하게 동작한다.
/// 클릭하면 <see cref="PatternCopyScreen"/>을 화면 스택에 push한다.
/// <para>
/// 무한 트레이닝은 "플레이" 서브메뉴에 있다 (<see cref="InfiniteTrainingMenuButtonPatch"/>).
/// 두 패치는 서로 다른 <c>ButtonSystemState</c>를 쓰는 것 외에는 구조가 같다.
/// </para>
/// </summary>
[HarmonyPatch]
public static class PatternCopyMenuButtonPatch
{
    private const string BUTTON_TEXT = "패턴 복제";

    /// <summary>편집 서브메뉴의 주황 계열을 잇는다 (비트맵 에디터 238,170,0 → 스킨 에디터 220,160,0 → 이것).</summary>
    private static readonly Color4 button_colour = new Color4(202, 150, 0, 255);

    public static MethodBase? TargetMethod() => AccessTools.Method(typeof(ButtonSystem), "load");

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(ButtonSystem __instance)
    {
        try
        {
            AddPatternCopyButton(__instance);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] PatternCopyMenuButtonPatch.Postfix failed: {ex}");
        }
    }

    private static void AddPatternCopyButton(ButtonSystem buttons)
    {
        if (!AccessHelper.TryGet<ButtonArea>(typeof(ButtonSystem), "buttonArea", buttons, out var buttonArea) || buttonArea == null)
        {
            HookLog.Write("[LazerSR] PatternCopyMenuButtonPatch: buttonArea not found.");
            return;
        }

        // 트리거 키는 부여하지 않는다 (params Key[] 생략 → TriggerKeys 빈 배열).
        // 편집 서브메뉴는 이미 B/E(비트맵)와 S(스킨)를 쓰고 있어 충돌을 피한다.
        var button = new MainMenuButton(
            BUTTON_TEXT,
            @"button-default-select",
            FontAwesome.Solid.Clone,
            button_colour,
            (_, _) => PushPatternCopyScreen(buttons))
        {
            VisibleState = ButtonSystemState.Edit,
            // ButtonSystem.load()가 자기 버튼들에 적용하는 것과 동일 (원본 :195-202).
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        // ButtonArea.Content는 Flow이므로 Add는 곧 플로우 맨 끝(= 스킨 에디터 오른쪽) 삽입이다.
        buttonArea.Add(button);

        // buttonsEdit에도 등록해 목록 기반 동작이 이 버튼도 함께 보게 한다.
        // (Edit 상태의 로고 클릭은 buttonsEdit.First() = 비트맵 에디터를 쓰므로 영향 없음.
        //  osu! 원본의 StopSamplePlayback()은 애초에 buttonsEdit을 훑지 않는다 — 편집 버튼 공통 성질.)
        if (AccessHelper.TryGet<IList>(typeof(ButtonSystem), "buttonsEdit", buttons, out var buttonsEdit) && buttonsEdit != null)
            buttonsEdit.Add(button);
        else
            HookLog.Write("[LazerSR] PatternCopyMenuButtonPatch: buttonsEdit not found.");

        HookLog.Write("[LazerSR] PatternCopyMenuButtonPatch: button added.");
    }

    /// <summary>
    /// 버튼이 속한 화면(= MainMenu)을 부모 체인에서 찾아 그 위에 진입 화면을 push한다.
    /// MainMenu.OnSuspending이 push에 반응해 버튼 슬라이드아웃/페이드를 처리하므로
    /// 전환 연출은 에디터 버튼과 동일하게 나온다.
    /// </summary>
    private static void PushPatternCopyScreen(ButtonSystem buttons)
    {
        try
        {
            Drawable? current = buttons;
            while (current != null && current is not OsuScreen)
                current = current.Parent;

            if (current is not OsuScreen screen)
            {
                HookLog.Write("[LazerSR] PatternCopyMenuButtonPatch: parent OsuScreen not found.");
                return;
            }

            // 이미 다른 화면으로 넘어가는 중이면 push하지 않는다 (연타 방지 겸용).
            if (!screen.IsCurrentScreen())
                return;

            screen.Push(new PatternCopyScreen());
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] PatternCopyMenuButtonPatch.PushPatternCopyScreen failed: {ex}");
        }
    }
}
