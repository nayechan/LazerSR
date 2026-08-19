using LazerSR.Hook;
using NUnit.Framework;

namespace LazerSR.Hook.Tests;

[TestFixture]
public sealed class HookBootstrapTests
{
    [Test]
    public void CreateHarmonyUsesConfiguredHarmonyId()
    {
        HookBootstrap hookBootstrap = new();

        Assert.That(hookBootstrap.CreateHarmony().Id, Is.EqualTo(HookBootstrap.HarmonyId));
    }
}
