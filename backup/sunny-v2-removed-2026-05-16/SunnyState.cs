using LazerSR.Hook.Data;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LazerSR.Hook.Tests")]

namespace LazerSR.Hook;

public enum SunnyFeature { Sunny, Dan, Graph, Honey, Msd }

public interface ISunnyStateSubscriber
{
    void OnEnabledChanged(bool enabled);
    void OnFeatureChanged(SunnyFeature feature, bool enabled) { }
}

public static class SunnyState
{
    private static readonly object _lock = new();
    private static readonly List<WeakReference<ISunnyStateSubscriber>> _subscribers = new();

    public static bool Enabled { get; private set; }

    // Feature toggles
    public static bool DanEnabled { get; private set; }
    public static bool StrainGraphEnabled { get; private set; }
    public static bool HoneySpotsEnabled { get; private set; }
    public static bool MsdEnabled { get; private set; }
    // Computed data (written by background tasks, read by patch drawables)
    private static volatile StrainGraphData? _strainData;
    public static StrainGraphData? StrainData { get => _strainData; set => _strainData = value; }

    public static void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            Enabled = enabled;
            NotifyEnabledChanged(enabled);
        }
    }

    public static void SetFeatureEnabled(SunnyFeature feature, bool enabled)
    {
        lock (_lock)
        {
            switch (feature)
            {
                case SunnyFeature.Sunny: Enabled = enabled; break;
                case SunnyFeature.Dan: DanEnabled = enabled; break;
                case SunnyFeature.Graph: StrainGraphEnabled = enabled; break;
                case SunnyFeature.Honey: HoneySpotsEnabled = enabled; break;
                case SunnyFeature.Msd: MsdEnabled = enabled; break;
            }

            if (feature == SunnyFeature.Sunny)
            {
                // Sunny maps to global Enabled — notify via OnEnabledChanged only, not OnFeatureChanged
                NotifyEnabledChanged(enabled);
                return;
            }

            var dead = new List<WeakReference<ISunnyStateSubscriber>>();
            foreach (var weakRef in _subscribers)
            {
                if (weakRef.TryGetTarget(out var subscriber))
                    subscriber.OnFeatureChanged(feature, enabled);
                else
                    dead.Add(weakRef);
            }
            foreach (var weakRef in dead)
                _subscribers.Remove(weakRef);
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
            DanEnabled = false;
            StrainGraphEnabled = false;
            HoneySpotsEnabled = false;
            MsdEnabled = false;
            StrainData = null;
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
