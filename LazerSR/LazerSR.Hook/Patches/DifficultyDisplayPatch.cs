using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LazerSR.Hook.Clones;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game;
using osu.Game.Graphics.Containers;
using osu.Game.Screens.Select;

namespace LazerSR.Hook.Patches;

// Sibling-Clone: patch BeatmapTitleWedge.LoadComplete and add a SunnyDifficultyDisplay
// as a new sibling of the original DifficultyDisplay inside the wedge's vertical FillFlowContainer.
// The original DifficultyDisplay is left untouched.
[HarmonyPatch]
public static class DifficultyDisplayPatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Screens.Select.BeatmapTitleWedge";

    private static readonly PropertyInfo? _internalChildrenProp =
        AccessTools.Property(typeof(CompositeDrawable), "InternalChildren");

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
            if (__instance is not CompositeDrawable wedge)
                return;

            Drawable? originalDisplay = FindOriginalDifficultyDisplay(wedge);
            if (originalDisplay == null)
            {
                Trace.WriteLine("[LazerSR] DifficultyDisplayPatch: original DifficultyDisplay not found inside BeatmapTitleWedge.");
                return;
            }

            FillFlowContainer? rowFlow = FindFillFlowAncestor(originalDisplay);
            if (rowFlow == null)
            {
                Trace.WriteLine("[LazerSR] DifficultyDisplayPatch: FillFlowContainer ancestor not found.");
                return;
            }

            var clone = new SunnyDifficultyDisplay();

            // Mirror the original DifficultyDisplay's wrapping in BeatmapTitleWedge.load():
            //
            //   new ShearAligningWrapper(new Container
            //   {
            //       Shear = -OsuGame.SHEAR,
            //       RelativeSizeAxes = Axes.X,
            //       AutoSizeAxes = Axes.Y,
            //       Margin = new MarginPadding { Left = -SongSelect.WEDGE_CONTENT_MARGIN },
            //       Padding = new MarginPadding { Right = -SongSelect.WEDGE_CONTENT_MARGIN },
            //       Child = new DifficultyDisplay(),
            //   })
            var wrapper = new ShearAligningWrapper(new Container
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Margin = new MarginPadding { Left = -SongSelect.WEDGE_CONTENT_MARGIN },
                Padding = new MarginPadding { Right = -SongSelect.WEDGE_CONTENT_MARGIN },
                Child = clone,
            });

            rowFlow.Add(wrapper);
        }
        catch (Exception e)
        {
            Trace.WriteLine($"[LazerSR] DifficultyDisplayPatch.Postfix failed: {e}");
        }
    }

    private static Drawable? FindOriginalDifficultyDisplay(Drawable root)
    {
        if (root.GetType().Name == "DifficultyDisplay")
            return root;

        if (root is CompositeDrawable composite)
        {
            var children = GetInternalChildren(composite);
            for (int i = 0; i < children.Count; i++)
            {
                var found = FindOriginalDifficultyDisplay(children[i]);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static List<Drawable> GetInternalChildren(CompositeDrawable composite)
    {
        var result = new List<Drawable>();
        if (_internalChildrenProp?.GetValue(composite) is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is Drawable d)
                    result.Add(d);
            }
        }
        return result;
    }

    private static FillFlowContainer? FindFillFlowAncestor(Drawable d)
    {
        Drawable? current = d.Parent;
        while (current != null)
        {
            if (current is FillFlowContainer match)
                return match;
            current = current.Parent;
        }
        return null;
    }
}
