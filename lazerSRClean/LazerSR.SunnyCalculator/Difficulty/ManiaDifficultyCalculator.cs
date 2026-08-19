// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Difficulty.Skills;
using osu.Game.Rulesets.Mania.MathUtils;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Mania.Difficulty
{
    // Intentionally does NOT inherit osu!'s DifficultyCalculator. That base class's abstract
    // members (CreateDifficultyAttributes/CreateSkills/CreateDifficultyHitObjects) are a binary
    // contract that osu! has changed across releases (e.g. dropping the clockRate parameter in
    // 2026.7xx), which broke this class when it inherited them. Re-implementing the calculate
    // loop locally keeps LazerSR decoupled from that contract.
    public class ManiaDifficultyCalculator
    {
        private const double difficulty_multiplier = 0.975;

        private readonly IRulesetInfo ruleset;
        private readonly IWorkingBeatmap workingBeatmap;

        public ManiaDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
        {
            this.ruleset = ruleset;
            workingBeatmap = beatmap;
        }

        public ManiaDifficultyAttributes Calculate(IEnumerable<Mod> mods, CancellationToken cancellationToken = default)
        {
            Mod[] playableMods = mods.Select(m => m.DeepClone()).ToArray();
            IBeatmap beatmap = workingBeatmap.GetPlayableBeatmap(ruleset, playableMods, cancellationToken);

            if (beatmap.HitObjects.Count == 0)
                return new ManiaDifficultyAttributes { Mods = playableMods };

            double clockRate = ModUtils.CalculateRateWithMods(playableMods);
            var skill = new Strain(playableMods);

            foreach (var dho in CreateDifficultyHitObjects(beatmap, clockRate))
            {
                cancellationToken.ThrowIfCancellationRequested();
                skill.Process(dho);
            }

            return CreateDifficultyAttributes(beatmap.HitObjects, playableMods, skill);
        }

        public List<TimedDifficultyAttributes> CalculateTimed(IEnumerable<Mod> mods, CancellationToken cancellationToken = default)
        {
            var attribs = new List<TimedDifficultyAttributes>();

            Mod[] playableMods = mods.Select(m => m.DeepClone()).ToArray();
            IBeatmap beatmap = workingBeatmap.GetPlayableBeatmap(ruleset, playableMods, cancellationToken);

            if (beatmap.HitObjects.Count == 0)
                return attribs;

            double clockRate = ModUtils.CalculateRateWithMods(playableMods);
            var skill = new Strain(playableMods);
            var progressiveHitObjects = new List<HitObject>();
            var difficultyObjects = CreateDifficultyHitObjects(beatmap, clockRate).ToArray();

            int currentIndex = 0;

            foreach (var obj in beatmap.HitObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progressiveHitObjects.Add(obj);

                while (currentIndex < difficultyObjects.Length && difficultyObjects[currentIndex].BaseObject.GetEndTime() <= obj.GetEndTime())
                {
                    skill.Process(difficultyObjects[currentIndex]);
                    currentIndex++;
                }

                attribs.Add(new TimedDifficultyAttributes(obj.GetEndTime(), CreateDifficultyAttributes(progressiveHitObjects, playableMods, skill)));
            }

            return attribs;
        }

        private static ManiaDifficultyAttributes CreateDifficultyAttributes(IReadOnlyList<HitObject> hitObjects, Mod[] mods, Strain skill)
        {
            if (hitObjects.Count == 0)
                return new ManiaDifficultyAttributes { Mods = mods };

            return new ManiaDifficultyAttributes
            {
                StarRating = skill.DifficultyValue() * difficulty_multiplier,
                Mods = mods,
                MaxCombo = hitObjects.Sum(maxComboForObject),
            };
        }

        private static int maxComboForObject(HitObject hitObject)
        {
            if (hitObject is HoldNote hold)
                return 1 + (int)((hold.EndTime - hold.StartTime) / 100);

            return 1;
        }

        private static IEnumerable<ManiaDifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
        {
            var sortedObjects = beatmap.HitObjects.ToArray();
            int totalColumns = ((ManiaBeatmap)beatmap).TotalColumns;

            LegacySortHelper<HitObject>.Sort(sortedObjects,
                Comparer<HitObject>.Create((a, b) => (int)Math.Round(a.StartTime) - (int)Math.Round(b.StartTime)));

            var objects = new List<ManiaDifficultyHitObject>();
            List<ManiaDifficultyHitObject>[] perColumnObjects = new List<ManiaDifficultyHitObject>[totalColumns];

            for (int column = 0; column < totalColumns; column++)
                perColumnObjects[column] = new List<ManiaDifficultyHitObject>();

            for (int i = 1; i < sortedObjects.Length; i++)
            {
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

            ManiaDifficultyPreprocessor.ProcessAndAssign(objects, beatmap);

            return objects;
        }
    }
}
