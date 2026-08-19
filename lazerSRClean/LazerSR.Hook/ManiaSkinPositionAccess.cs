using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using osu.Game.Skinning;

namespace LazerSR.Hook;

/// <summary>
/// Reflection helpers for mutating a legacy mania skin's <see cref="LegacyManiaSkinConfiguration"/>
/// (HitPosition/ScorePosition etc.) at runtime and notifying listeners to re-query it.
/// Session-only: nothing here is ever written back to skin.ini.
/// </summary>
internal static class ManiaSkinPositionAccess
{
    private static readonly FieldInfo? ManiaConfigurationsField =
        AccessTools.Field(typeof(LegacySkin), "ManiaConfigurations");

    private static readonly FieldInfo? SourceChangedField =
        AccessTools.Field(typeof(SkinManager), "SourceChanged");

    internal static LegacyManiaSkinConfiguration? GetOrCreateConfiguration(Skin skin, int keys)
    {
        if (skin is not LegacySkin legacySkin) return null;

        if (ManiaConfigurationsField?.GetValue(legacySkin) is not Dictionary<int, LegacyManiaSkinConfiguration> configs)
            return null;

        if (!configs.TryGetValue(keys, out var config))
            configs[keys] = config = new LegacyManiaSkinConfiguration(keys);

        return config;
    }

    /// <summary>
    /// Fires <see cref="SkinManager"/>'s <c>SourceChanged</c> event so already-loaded consumers
    /// (e.g. the mania playfield's hit target) re-read the config they cached and reposition immediately.
    /// </summary>
    internal static void NotifySkinChanged(SkinManager skinManager)
    {
        (SourceChangedField?.GetValue(skinManager) as Action)?.Invoke();
    }
}
