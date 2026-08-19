using System;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps.Drawables;

namespace LazerSR.Hook.Patches;

[HarmonyPatch]
public static class BeatmapAttributesDisplayPatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Overlays.Mods.BeatmapAttributesDisplay";

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
        return type == null ? null : AccessTools.Method(type, "LoadComplete");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            if (__instance is Drawable owner)
                InsertSunnyPill_2_1(owner);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] BeatmapAttributesDisplayPatch.Postfix failed: {ex}");
        }
    }

    private static void InsertSunnyPill_2_1(Drawable owner)
    {
        try
        {
            Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
            if (type == null) return;

            var srField = AccessTools.Field(type, "starRatingDisplay");
            var sr = srField?.GetValue(owner) as Drawable;
            if (sr == null)
            {
                HookLog.Write("[LazerSR] InsertSunnyPill_2_1: starRatingDisplay not found.");
                return;
            }

            var parentProp = AccessTools.Property(typeof(Drawable), "Parent");
            var parent = parentProp?.GetValue(sr) as CompositeDrawable;
            if (parent == null)
            {
                HookLog.Write("[LazerSR] InsertSunnyPill_2_1: parent not found.");
                return;
            }

            var pill = new StarRatingDisplay(default, animated: true)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };
            pill.Current = SunnyState.CurrentSr;

            var insertMethod = AccessTools.Method(parent.GetType(), "Insert", new[] { typeof(int), typeof(Drawable) });
            insertMethod?.Invoke(parent, new object[] { 1, pill });

            HookLog.Write("[LazerSR] InsertSunnyPill_2_1: pill inserted.");
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] InsertSunnyPill_2_1 failed: {ex}");
        }
    }
}
