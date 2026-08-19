using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace LazerSR.Hook;

public static class Patcher
{
    private static int _patched;

    public static void Apply()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

        bool osuGameLoaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .Any(a => a.GetName().Name == "osu.Game");

        if (osuGameLoaded)
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            ApplyPatch();
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (args.LoadedAssembly.GetName().Name != "osu.Game")
            return;

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        ApplyPatch();
    }

    private static void ApplyPatch()
    {
        if (Interlocked.CompareExchange(ref _patched, 1, 0) != 0)
            return;

        try
        {
            Harmony harmony = new Harmony(HookBootstrap.HarmonyId);
            harmony.PatchAll(typeof(Patcher).Assembly);
            Interlocked.Exchange(ref _patched, 2);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[LazerSR] Patcher.ApplyPatch failed: {ex}");
            Interlocked.Exchange(ref _patched, 0);
        }
    }
}
