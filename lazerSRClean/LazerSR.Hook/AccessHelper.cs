using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace LazerSR.Hook;

internal static class AccessHelper
{
    internal static bool TryGet<T>(Type type, string name, object instance, out T? value)
    {
        PropertyInfo? prop = AccessTools.Property(type, name);
        if (prop != null)
        {
            value = (T?)prop.GetValue(instance);
            return true;
        }

        FieldInfo? field = AccessTools.Field(type, name);
        if (field != null)
        {
            value = (T?)field.GetValue(instance);
            return true;
        }

        FieldInfo? backing = AccessTools.Field(type, $"<{name}>k__BackingField");
        if (backing != null)
        {
            value = (T?)backing.GetValue(instance);
            return true;
        }

        value = default;
        return false;
    }

    internal static Drawable? FindFirstChildOfType(Drawable root, Type targetType)
    {
        if (targetType.IsInstanceOfType(root)) return root;
        if (root is not CompositeDrawable composite) return null;

        var prop = AccessTools.Property(typeof(CompositeDrawable), "InternalChildren");
        if (prop?.GetValue(composite) is not IEnumerable children) return null;

        foreach (var child in children)
        {
            if (child is Drawable d)
            {
                var found = FindFirstChildOfType(d, targetType);
                if (found != null) return found;
            }
        }
        return null;
    }
}
