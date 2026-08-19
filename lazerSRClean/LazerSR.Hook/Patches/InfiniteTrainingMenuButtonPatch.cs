using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using LazerSR.Hook.Screens;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osuTK.Graphics;

namespace LazerSR.Hook.Patches;

/// <summary>
/// 메인 메뉴 "플레이" 서브메뉴(솔로/멀티/플레이리스트/일일도전)에 '무한 트레이닝' 버튼을 추가한다.
/// osu! 원본 <see cref="MainMenuButton"/>를 그대로 인스턴스화하므로 호버 애니메이션/비트 싱크/
/// 사운드/확장·축소 상태머신이 다른 버튼과 완전히 동일하게 동작한다.
/// 클릭하면 <see cref="InfiniteTrainingScreen"/>을 화면 스택에 push한다.
/// </summary>
[HarmonyPatch]
public static class InfiniteTrainingMenuButtonPatch
{
    private const string BUTTON_TEXT = "무한 트레이닝";

    /// <summary>다른 플레이 서브메뉴 버튼(멀티/플레이리스트/일일도전)과 동일한 색상.</summary>
    private static readonly Color4 button_colour = new Color4(94, 63, 186, 255);

    public static MethodBase? TargetMethod() => AccessTools.Method(typeof(ButtonSystem), "load");

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(ButtonSystem __instance)
    {
        try
        {
            AddInfiniteTrainingButton(__instance);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] InfiniteTrainingMenuButtonPatch.Postfix failed: {ex}");
        }
    }

    private static void AddInfiniteTrainingButton(ButtonSystem buttons)
    {
        if (!AccessHelper.TryGet<ButtonArea>(typeof(ButtonSystem), "buttonArea", buttons, out var buttonArea) || buttonArea == null)
        {
            HookLog.Write("[LazerSR] InfiniteTrainingMenuButtonPatch: buttonArea not found.");
            return;
        }

        // 트리거 키는 부여하지 않는다 (params Key[] 생략 → TriggerKeys 빈 배열).
        var button = new MainMenuButton(
            BUTTON_TEXT,
            @"button-default-select",
            OsuIcon.Player,
            button_colour,
            (_, _) => PushInfiniteTrainingScreen(buttons))
        {
            VisibleState = ButtonSystemState.Play,
            // ButtonSystem.load()가 자기 버튼들에 적용하는 것과 동일 (원본 :195-202).
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        // ButtonArea.Content는 Flow이므로 Add는 곧 플로우 맨 끝(= 일일도전 오른쪽) 삽입이다.
        buttonArea.Add(button);

        // buttonsPlay에도 등록해 StopSamplePlayback()이 다른 버튼과 동일하게 이 버튼도 커버하게 한다.
        // (Play 상태의 로고 클릭은 buttonsPlay.First() = 솔로를 그대로 사용하므로 영향 없음.)
        if (AccessHelper.TryGet<IList>(typeof(ButtonSystem), "buttonsPlay", buttons, out var buttonsPlay) && buttonsPlay != null)
            buttonsPlay.Add(button);
        else
            HookLog.Write("[LazerSR] InfiniteTrainingMenuButtonPatch: buttonsPlay not found (sample playback stop not wired).");

        HookLog.Write("[LazerSR] InfiniteTrainingMenuButtonPatch: button added.");
    }

    /// <summary>
    /// 버튼이 속한 화면(= MainMenu)을 부모 체인에서 찾아 그 위에 대기 화면을 push한다.
    /// MainMenu.OnSuspending이 push에 반응해 버튼 슬라이드아웃/페이드를 처리하므로
    /// 전환 연출은 솔로 플레이 버튼과 동일하게 나온다.
    /// </summary>
    private static void PushInfiniteTrainingScreen(ButtonSystem buttons)
    {
        try
        {
            Drawable? current = buttons;
            while (current != null && current is not OsuScreen)
                current = current.Parent;

            if (current is not OsuScreen screen)
            {
                HookLog.Write("[LazerSR] InfiniteTrainingMenuButtonPatch: parent OsuScreen not found.");
                return;
            }

            // 이미 다른 화면으로 넘어가는 중이면 push하지 않는다 (연타 방지 겸용).
            if (!screen.IsCurrentScreen())
                return;

            screen.Push(new InfiniteTrainingScreen());
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] InfiniteTrainingMenuButtonPatch.PushInfiniteTrainingScreen failed: {ex}");
        }
    }
}
