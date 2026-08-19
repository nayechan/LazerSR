// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using LazerSR.Sunny.Mania.Preprocessing;

namespace LazerSR.Sunny.Mania.Evaluators
{
    public class UnevennessEvaluator
    {
        public static double GetValueOf(ManiaDifficultyHitObject current)
        {
            var data = current.DifficultyData;
            return data.SampleFeatureAtTime(current.StartTime, data.Unevenness);
        }
    }
}
