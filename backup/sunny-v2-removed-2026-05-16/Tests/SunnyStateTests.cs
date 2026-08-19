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
        // Register a subscriber and let it be GC'd
        RegisterAndForget();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Should not throw even though the subscriber is gone
        Assert.That(() => SunnyState.SetEnabled(true), Throws.Nothing);
    }

    [Test]
    public void SetEnabled_DeadWeakRefs_AreCleanedUp()
    {
        RegisterAndForget();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Second call should still work normally
        var liveSubscriber = new FakeSubscriber();
        SunnyState.Register(liveSubscriber);

        SunnyState.SetEnabled(true);

        Assert.That(liveSubscriber.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void DanEnabled_DefaultsFalse()
    {
        Assert.That(SunnyState.DanEnabled, Is.False);
    }

    [Test]
    public void SetFeatureEnabled_Dan_UpdatesProperty()
    {
        SunnyState.SetFeatureEnabled(SunnyFeature.Dan, true);
        Assert.That(SunnyState.DanEnabled, Is.True);
    }

    [Test]
    public void SetFeatureEnabled_Graph_UpdatesProperty()
    {
        SunnyState.SetFeatureEnabled(SunnyFeature.Graph, true);
        Assert.That(SunnyState.StrainGraphEnabled, Is.True);
    }

    [Test]
    public void SetFeatureEnabled_Honey_UpdatesProperty()
    {
        SunnyState.SetFeatureEnabled(SunnyFeature.Honey, true);
        Assert.That(SunnyState.HoneySpotsEnabled, Is.True);
    }

    [Test]
    public void SetFeatureEnabled_NotifiesSubscriber()
    {
        var sub = new FeatureFakeSubscriber();
        SunnyState.Register(sub);

        SunnyState.SetFeatureEnabled(SunnyFeature.Dan, true);

        Assert.That(sub.LastFeature, Is.EqualTo(SunnyFeature.Dan));
        Assert.That(sub.LastEnabled, Is.True);
        Assert.That(sub.FeatureCallCount, Is.EqualTo(1));
    }

    // Isolated helper so the local ref goes out of scope before GC
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

    private sealed class FeatureFakeSubscriber : ISunnyStateSubscriber
    {
        public SunnyFeature LastFeature { get; private set; }
        public bool LastEnabled { get; private set; }
        public int FeatureCallCount { get; private set; }
        public void OnEnabledChanged(bool enabled) { }
        public void OnFeatureChanged(SunnyFeature feature, bool enabled)
        {
            LastFeature = feature;
            LastEnabled = enabled;
            FeatureCallCount++;
        }
    }
}
