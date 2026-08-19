using System;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using LazerSR.Hook.Widgets;
using osu.Game.Rulesets;
using osu.Game.Skinning;

namespace LazerSR.Hook.Patches;

[HarmonyPatch(typeof(SerialisedDrawableInfo), nameof(SerialisedDrawableInfo.GetAllAvailableDrawables))]
public static class SkinWidgetRegistrarPatch
{
    private static readonly Type[] LazerSRWidgets = [typeof(BlackBoxWidget), typeof(StrainGraphWidget)];

    public static void Postfix(ref Type[] __result, RulesetInfo? ruleset)
    {
        try
        {
            if (ruleset != null && ruleset.ShortName != "mania")
                return;

            __result = __result.Concat(LazerSRWidgets).OrderBy(t => t.Name).ToArray();
        }
        catch (Exception e)
        {
            Trace.WriteLine($"[LazerSR] SkinWidgetRegistrarPatch.Postfix failed: {e}");
        }
    }
}
