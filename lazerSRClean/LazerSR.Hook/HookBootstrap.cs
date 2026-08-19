using HarmonyLib;

namespace LazerSR.Hook;

public sealed class HookBootstrap
{
    public const string HarmonyId = "dev.lazersr.hook";

    public Harmony CreateHarmony() => new(HarmonyId);
}
