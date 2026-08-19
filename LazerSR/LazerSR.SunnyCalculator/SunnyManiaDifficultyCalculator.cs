using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LazerSR.Sunny.Mania.Preprocessing;
using LazerSR.Sunny.Mania.Skills;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Utils;

namespace LazerSR.SunnyCalculator;

public sealed class SunnyManiaDifficultyCalculator
{
    private const double difficulty_multiplier = 0.975;

    public double Calculate(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null, CancellationToken cancellationToken = default)
    {
        if (beatmap is not ManiaBeatmap maniaBeatmap)
            throw new ArgumentException("Beatmap must be a mania beatmap.", nameof(beatmap));

        cancellationToken.ThrowIfCancellationRequested();

        Mod[] playableMods = mods?.ToArray() ?? Array.Empty<Mod>();
        double clockRate = ModUtils.CalculateRateWithMods(playableMods);

        if (maniaBeatmap.HitObjects.Count == 0)
            return 0;

        Strain strain = new(playableMods);

        foreach (var hitObject in createDifficultyHitObjects(maniaBeatmap, clockRate, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            strain.Process(hitObject);
        }

        return strain.DifficultyValue() * difficulty_multiplier;
    }

    public (double Time, double Strain)[] GetStrainTimeline(
        IBeatmap beatmap,
        IReadOnlyList<Mod>? mods = null,
        CancellationToken cancellationToken = default)
    {
        if (beatmap is not ManiaBeatmap maniaBeatmap)
            throw new ArgumentException("Beatmap must be a mania beatmap.", nameof(beatmap));

        cancellationToken.ThrowIfCancellationRequested();

        Mod[] playableMods = mods?.ToArray() ?? Array.Empty<Mod>();
        double clockRate = ModUtils.CalculateRateWithMods(playableMods);

        if (maniaBeatmap.HitObjects.Count == 0)
            return Array.Empty<(double, double)>();

        var strain = new Strain(playableMods);
        var timeline = new List<(double, double)>();

        foreach (var hitObject in createDifficultyHitObjects(maniaBeatmap, clockRate, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            double strainValue = strain.Process(hitObject);
            timeline.Add((hitObject.StartTime, strainValue));
        }

        return timeline.ToArray();
    }

    private static IEnumerable<DifficultyHitObject> createDifficultyHitObjects(ManiaBeatmap beatmap, double clockRate, CancellationToken cancellationToken)
    {
        var sortedObjects = beatmap.HitObjects.Cast<HitObject>().ToArray();
        int totalColumns = beatmap.TotalColumns;

        cancellationToken.ThrowIfCancellationRequested();
        legacySort(sortedObjects, Comparer<HitObject>.Create((a, b) => (int)Math.Round(a.StartTime) - (int)Math.Round(b.StartTime)));
        cancellationToken.ThrowIfCancellationRequested();

        var objects = new List<DifficultyHitObject>();
        List<DifficultyHitObject>[] perColumnObjects = new List<DifficultyHitObject>[totalColumns];

        for (int column = 0; column < totalColumns; column++)
            perColumnObjects[column] = new List<DifficultyHitObject>();

        for (int i = 1; i < sortedObjects.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var maniaDifficultyHitObject = new ManiaDifficultyHitObject(
                sortedObjects[i],
                sortedObjects[i - 1],
                clockRate,
                objects,
                perColumnObjects,
                objects.Count
            );

            objects.Add(maniaDifficultyHitObject);
            perColumnObjects[maniaDifficultyHitObject.Column].Add(maniaDifficultyHitObject);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ManiaDifficultyPreprocessor.ProcessAndAssign(objects, beatmap, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return objects;
    }

    private static void legacySort<T>(T[] keys, IComparer<T> comparer)
    {
        if (keys.Length == 0)
            return;

        depthLimitedQuickSort(keys, 0, keys.Length - 1, comparer, 32);
    }

    private static void depthLimitedQuickSort<T>(T[] keys, int left, int right, IComparer<T> comparer, int depthLimit)
    {
        do
        {
            if (depthLimit == 0)
            {
                heapSort(keys, left, right, comparer);
                return;
            }

            int i = left;
            int j = right;
            int middle = i + ((j - i) >> 1);

            swapIfGreater(keys, comparer, i, middle);
            swapIfGreater(keys, comparer, i, j);
            swapIfGreater(keys, comparer, middle, j);

            T pivot = keys[middle];

            do
            {
                while (comparer.Compare(keys[i], pivot) < 0) i++;
                while (comparer.Compare(pivot, keys[j]) < 0) j--;

                if (i > j)
                    break;

                if (i < j)
                    (keys[i], keys[j]) = (keys[j], keys[i]);

                i++;
                j--;
            } while (i <= j);

            depthLimit--;

            if (j - left <= right - i)
            {
                if (left < j)
                    depthLimitedQuickSort(keys, left, j, comparer, depthLimit);

                left = i;
            }
            else
            {
                if (i < right)
                    depthLimitedQuickSort(keys, i, right, comparer, depthLimit);

                right = j;
            }
        } while (left < right);
    }

    private static void heapSort<T>(T[] keys, int lo, int hi, IComparer<T> comparer)
    {
        int n = hi - lo + 1;

        for (int i = n / 2; i >= 1; i--)
            downHeap(keys, i, n, lo, comparer);

        for (int i = n; i > 1; i--)
        {
            (keys[lo], keys[lo + i - 1]) = (keys[lo + i - 1], keys[lo]);
            downHeap(keys, 1, i - 1, lo, comparer);
        }
    }

    private static void downHeap<T>(T[] keys, int i, int n, int lo, IComparer<T> comparer)
    {
        T d = keys[lo + i - 1];

        while (i <= n / 2)
        {
            int child = 2 * i;

            if (child < n && comparer.Compare(keys[lo + child - 1], keys[lo + child]) < 0)
                child++;

            if (comparer.Compare(d, keys[lo + child - 1]) >= 0)
                break;

            keys[lo + i - 1] = keys[lo + child - 1];
            i = child;
        }

        keys[lo + i - 1] = d;
    }

    private static void swapIfGreater<T>(T[] keys, IComparer<T> comparer, int a, int b)
    {
        if (a != b && comparer.Compare(keys[a], keys[b]) > 0)
            (keys[a], keys[b]) = (keys[b], keys[a]);
    }
}
