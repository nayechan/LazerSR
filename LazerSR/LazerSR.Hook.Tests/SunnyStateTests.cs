using LazerSR.Hook;
using NUnit.Framework;

namespace LazerSR.Hook.Tests;

[TestFixture]
public sealed class SunnyStateTests
{
    [SetUp]
    public void SetUp()
    {
        SunnyState.ResetForTesting();
    }

    [Test]
    public void SetEnabled_True_EnabledIsTrue()
    {
        SunnyState.SetEnabled(true);

        Assert.That(SunnyState.Enabled, Is.True);
    }

    [Test]
    public void SetEnabled_False_EnabledIsFalse()
    {
        SunnyState.SetEnabled(true);
        SunnyState.SetEnabled(false);

        Assert.That(SunnyState.Enabled, Is.False);
    }

    [Test]
    public void Register_Subscriber_ReceivesOnEnabledChangedOnSetEnabled()
    {
        var subscriber = new FakeSubscriber();
        SunnyState.Register(subscriber);

        SunnyState.SetEnabled(true);

        Assert.That(subscriber.LastEnabled, Is.True);
        Assert.That(subscriber.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void Register_Subscriber_ReceivesOnEnabledChanged_False()
    {
        SunnyState.SetEnabled(true);
        var subscriber = new FakeSubscriber();
        SunnyState.Register(subscriber);

        SunnyState.SetEnabled(false);

        Assert.That(subscriber.LastEnabled, Is.False);
        Assert.That(subscriber.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void SetEnabled_AfterSubscriberGCed_DoesNotThrow()
    {
        RegisterAndForget();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.That(() => SunnyState.SetEnabled(true), Throws.Nothing);
    }

    [Test]
    public void SetEnabled_DeadWeakRefs_AreCleanedUp()
    {
        RegisterAndForget();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var liveSubscriber = new FakeSubscriber();
        SunnyState.Register(liveSubscriber);

        SunnyState.SetEnabled(true);

        Assert.That(liveSubscriber.CallCount, Is.EqualTo(1));
    }

    private static void RegisterAndForget()
    {
        var subscriber = new FakeSubscriber();
        SunnyState.Register(subscriber);
    }

    private sealed class FakeSubscriber : ISunnyStateSubscriber
    {
        public bool LastEnabled { get; private set; }
        public int CallCount { get; private set; }

        public void OnEnabledChanged(bool enabled)
        {
            LastEnabled = enabled;
            CallCount++;
        }
    }
}
