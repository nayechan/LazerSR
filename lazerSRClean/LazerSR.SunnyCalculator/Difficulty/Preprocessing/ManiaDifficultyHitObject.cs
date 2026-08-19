// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing.Data;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Mania.Difficulty.Preprocessing
{
    // Intentionally does NOT inherit osu!'s DifficultyHitObject. That base class's constructor/
    // members are a binary contract osu! has already broken lazerSR against once before (see
    // ManiaDifficultyCalculator/Strain for the confirmed clockRate/ObjectStrains incidents), so
    // the small subset actually used here (chronological Previous(), hit-window lookup) is
    // re-implemented locally to stay decoupled from that contract.
    public class ManiaDifficultyHitObject
    {
        public int Index;
        public readonly ManiaHitObject BaseObject;
        public readonly HitObject LastObject;
        public readonly double DeltaTime;
        public readonly double StartTime;
        public readonly double EndTime;
        public readonly double ClockRate;
        public readonly double HitWindowGreat;

        public int Column => BaseObject.Column;
        public bool IsLong => EndTime > StartTime;

        private readonly List<ManiaDifficultyHitObject> allObjects;
        private readonly int columnIndex;
        public readonly List<ManiaDifficultyHitObject>[] PerColumnObjects;

        public ManiaDifficultyData DifficultyData;
        public int StrainTimePointIndex;

        public ManiaDifficultyHitObject(HitObject hitObject, HitObject lastObject, double clockRate, List<ManiaDifficultyHitObject> objects, List<ManiaDifficultyHitObject>[] perColumnObjects, int index)
        {
            allObjects = objects;
            Index = index;
            BaseObject = (ManiaHitObject)hitObject;
            LastObject = lastObject;
            DeltaTime = (hitObject.StartTime - lastObject.StartTime) / clockRate;
            StartTime = hitObject.StartTime / clockRate;
            EndTime = hitObject.GetEndTime() / clockRate;
            ClockRate = clockRate;
            HitWindowGreat = hitWindow(HitResult.Great);

            DifficultyData = new ManiaDifficultyData();
            PerColumnObjects = perColumnObjects;
            columnIndex = perColumnObjects[Column].Count;
        }

        /// <summary>
        /// The previous object in the full chronological list (regardless of column).
        /// </summary>
        /// <param name="skipCount">The number of objects to go back.</param>
        public ManiaDifficultyHitObject? Previous(int skipCount = 0)
        {
            int index = Index - (skipCount + 1);
            return index >= 0 && index < allObjects.Count ? allObjects[index] : null;
        }

        private double hitWindow(HitResult hitResult) => 2 * getRawHitWindow(hitResult) / ClockRate;

        private double getRawHitWindow(HitResult hitResult)
        {
            if (BaseObject.HitWindows != HitWindows.Empty)
                return BaseObject.HitWindows.WindowFor(hitResult);

            foreach (var nestedHitObject in BaseObject.NestedHitObjects)
            {
                if (nestedHitObject.HitWindows != HitWindows.Empty)
                    return nestedHitObject.HitWindows.WindowFor(hitResult);
            }

            return 0;
        }

        /// <summary>
        /// The previous object in the same column as this <see cref="ManiaDifficultyHitObject"/>, exclusive of Long Note tails.
        /// </summary>
        /// <param name="backwardsIndex">The number of notes to go back.</param>
        /// <returns>The object in this column <paramref name="backwardsIndex"/> notes back, or null if this is the first note in the column.</returns>
        public ManiaDifficultyHitObject? PrevInColumn(int backwardsIndex = 0)
        {
            int index = columnIndex - (backwardsIndex + 1);
            return index >= 0 && index < PerColumnObjects[Column].Count ? PerColumnObjects[Column][index] : null;
        }

        /// <summary>
        /// The next object in the same column as this <see cref="ManiaDifficultyHitObject"/>, exclusive of Long Note tails.
        /// </summary>
        /// <param name="forwardsIndex">The number of notes to go forward.</param>
        /// <returns>The object in this column <paramref name="forwardsIndex"/> notes forward, or null if this is the last note in the column.</returns>
        public ManiaDifficultyHitObject? NextInColumn(int forwardsIndex = 0)
        {
            int index = columnIndex + (forwardsIndex + 1);
            return index >= 0 && index < PerColumnObjects[Column].Count ? PerColumnObjects[Column][index] : null;
        }
    }
}
