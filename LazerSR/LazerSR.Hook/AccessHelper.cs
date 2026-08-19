using System.Reflection;
using HarmonyLib;

namespace LazerSR.Hook;

internal static class AccessHelper
{
    private const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

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
}
