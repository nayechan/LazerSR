using System;
using LazerSR.Hook;
using LazerSR.Hook.Ipc;
using LazerSR.SunnyCalculator.Tuning;

// Global namespace required by the DOTNET_STARTUP_HOOKS mechanism.
public class StartupHook
{
    public static void Initialize()
    {
        Run(DependencyResolver.Install, nameof(DependencyResolver.Install));
        Run(ApplySunnyPlusToggle, nameof(ApplySunnyPlusToggle));
        Run(PipeServer.StartBackground, nameof(PipeServer.StartBackground));
        Run(Patcher.Apply, nameof(Patcher.Apply));
    }

    // TEMP: sunny+ on/off checkbox in the Launcher, wired through via env var (child-process-only,
    // per the hard constraint against touching global/user env vars). Must run after
    // DependencyResolver.Install so the SunnyCalculator assembly can resolve.
    private static void ApplySunnyPlusToggle()
    {
        DiffCombiner.UniversalDiffEnabled = Environment.GetEnvironmentVariable("LAZERSR_SUNNYPLUS") != "0";
    }

    private static void Run(Action action, string name)
    {
        try { action(); }
        catch (Exception ex) { HookLog.Write($"[LazerSR] {name} failed: {ex}"); }
    }
}
