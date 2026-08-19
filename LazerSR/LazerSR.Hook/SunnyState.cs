using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LazerSR.Hook.Tests")]

namespace LazerSR.Hook;

public interface ISunnyStateSubscriber
{
    void OnEnabledChanged(bool enabled);
}

public static class SunnyState
{
    private static readonly object _lock = new();
    private static readonly List<WeakReference<ISunnyStateSubscriber>> _subscribers = new();

    public static bool Enabled { get; private set; }

    public static void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            Enabled = enabled;
            NotifyEnabledChanged(enabled);
        }
    }

    public static void Register(ISunnyStateSubscriber subscriber)
    {
        lock (_lock)
        {
            _subscribers.Add(new WeakReference<ISunnyStateSubscriber>(subscriber));
        }
    }

    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _subscribers.Clear();
            Enabled = false;
        }
    }

    private static void NotifyEnabledChanged(bool enabled)
    {
        var dead = new List<WeakReference<ISunnyStateSubscriber>>();
        foreach (var weakRef in _subscribers)
        {
            if (weakRef.TryGetTarget(out var subscriber))
                subscriber.OnEnabledChanged(enabled);
            else
                dead.Add(weakRef);
        }
        foreach (var weakRef in dead)
            _subscribers.Remove(weakRef);
    }
}
