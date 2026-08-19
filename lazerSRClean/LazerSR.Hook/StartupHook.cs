using System;
using LazerSR.Hook;
using LazerSR.Hook.Ipc;

// Global namespace required by the DOTNET_STARTUP_HOOKS mechanism.
public class StartupHook
{
    public static void Initialize()
    {
        Run(DependencyResolver.Install, nameof(DependencyResolver.Install));
        Run(PipeServer.StartBackground, nameof(PipeServer.StartBackground));
        Run(Patcher.Apply, nameof(Patcher.Apply));
    }

    private static void Run(Action action, string name)
    {
        try { action(); }
        catch (Exception ex) { HookLog.Write($"[LazerSR] {name} failed: {ex}"); }
    }
}
