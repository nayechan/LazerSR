using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Difficulty.Skills;
using osu.Game.Rulesets.Mania.MathUtils;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Utils;

namespace LazerSR.SunnyCalculator;

public sealed class SunnyManiaDifficultyCalculator
{
    /// <summary>
    /// <c>ManiaDifficultyCalculator</c>가 최종 star rating에 곱하는 값과 같아야 한다.
    /// 그쪽은 <c>IWorkingBeatmap</c>을 요구하므로, 메모리에서 만든 <c>IBeatmap</c>을 바로 평가하려면
    /// 계산 루프를 여기서 한 번 더 도는 편이 간단하다.
    /// </summary>
    private const double difficulty_multiplier = 0.975;

    /// <summary>
    /// 이미 playable한 <see cref="IBeatmap"/>의 sunnySR을 직접 계산한다.
    /// 합성 비트맵처럼 <c>WorkingBeatmap</c>이 없는 경우를 위한 경로다.
    /// </summary>
    /// <param name="weightedNoteCountOverride">
    /// Replaces the note count used by sunnySR's short map nerf. Pass the whole map's value when rating a
    /// slice of a map, so the slice isn't nerfed for its own length. Null keeps the default behaviour.
    /// </param>
    public double Calculate(
        IBeatmap beatmap,
        IReadOnlyList<Mod>? mods = null,
        CancellationToken token = default,
        double? weightedNoteCountOverride = null)
    {
        if (beatmap is not ManiaBeatmap maniaBeatmap || maniaBeatmap.HitObjects.Count == 0)
            return 0;

        Mod[] playableMods = mods?.ToArray() ?? Array.Empty<Mod>();
        double clockRate = ModUtils.CalculateRateWithMods(playableMods);

        var strain = new Strain(playableMods, weightedNoteCountOverride);

        foreach (var dho in CreateDifficultyHitObjects(maniaBeatmap, clockRate))
        {
            token.ThrowIfCancellationRequested();
            strain.Process(dho);
        }

        return strain.DifficultyValue() * difficulty_multiplier;
    }

    public (double Time, double Strain)[] GetStrainTimeline(
        IBeatmap beatmap,
        IReadOnlyList<Mod>? mods = null,
        CancellationToken token = default)
    {
        if (beatmap is not ManiaBeatmap maniaBeatmap)
            return Array.Empty<(double, double)>();

        token.ThrowIfCancellationRequested();

        Mod[] playableMods = mods?.ToArray() ?? Array.Empty<Mod>();
        double clockRate = ModUtils.CalculateRateWithMods(playableMods);

        if (maniaBeatmap.HitObjects.Count == 0)
            return Array.Empty<(double, double)>();

        var dhos = CreateDifficultyHitObjects(maniaBeatmap, clockRate).ToList();
        var strain = new Strain(playableMods);

        foreach (var dho in dhos)
        {
            token.ThrowIfCancellationRequested();
            strain.Process(dho);
        }

        var strains = strain.GetObjectStrains().ToArray();
        return dhos.Zip(strains, (dho, s) => (dho.BaseObject.StartTime, s)).ToArray();
    }

    /// <summary>
    /// The weighted note count sunnySR's short map nerf uses, without running the (far heavier) strain
    /// evaluation. Mirrors the accumulation in <c>Strain.StrainValueAt</c>: one unit per difficulty object
    /// plus a long note bonus, over the same object set (the chronologically first object never becomes a
    /// difficulty object, so it is skipped here too).
    /// </summary>
    public static double CalculateWeightedNoteCount(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
    {
        if (beatmap is not ManiaBeatmap maniaBeatmap || maniaBeatmap.HitObjects.Count < 2)
            return 0;

        double clockRate = ModUtils.CalculateRateWithMods(mods?.ToArray() ?? Array.Empty<Mod>());

        var sortedObjects = maniaBeatmap.HitObjects.Cast<HitObject>().ToArray();

        LegacySortHelper<HitObject>.Sort(sortedObjects,
            Comparer<HitObject>.Create((a, b) => (int)Math.Round(a.StartTime) - (int)Math.Round(b.StartTime)));

        double weighted = sortedObjects.Length - 1;

        for (int i = 1; i < sortedObjects.Length; i++)
        {
            double startTime = sortedObjects[i].StartTime / clockRate;
            double endTime = sortedObjects[i].GetEndTime() / clockRate;

            if (endTime > startTime)
                weighted += 0.5 * Math.Min(endTime - startTime, 1000.0) / 200.0;
        }

        return weighted;
    }

    private static IEnumerable<ManiaDifficultyHitObject> CreateDifficultyHitObjects(ManiaBeatmap beatmap, double clockRate)
    {
        var sortedObjects = beatmap.HitObjects.Cast<HitObject>().ToArray();
        int totalColumns = beatmap.TotalColumns;

        LegacySortHelper<HitObject>.Sort(sortedObjects,
            Comparer<HitObject>.Create((a, b) => (int)Math.Round(a.StartTime) - (int)Math.Round(b.StartTime)));

        var objects = new List<ManiaDifficultyHitObject>();
        List<ManiaDifficultyHitObject>[] perColumnObjects = new List<ManiaDifficultyHitObject>[totalColumns];
        for (int column = 0; column < totalColumns; column++)
            perColumnObjects[column] = new List<ManiaDifficultyHitObject>();

        for (int i = 1; i < sortedObjects.Length; i++)
        {
            var dho = new ManiaDifficultyHitObject(
                sortedObjects[i], sortedObjects[i - 1], clockRate,
                objects, perColumnObjects, objects.Count);
            objects.Add(dho);
            perColumnObjects[dho.Column].Add(dho);
        }

        ManiaDifficultyPreprocessor.ProcessAndAssign(objects, beatmap);
        return objects;
    }
}
