using LazerSR.SunnyCalculator;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;

namespace LazerSR.SunnyCalculator.Tests;

[TestFixture]
public sealed class SunnyManiaDifficultyCalculatorTests
{
    [Test]
    public void EmptyManiaBeatmapReturnsZero()
    {
        SunnyManiaDifficultyCalculator calculator = new();
        ManiaBeatmap beatmap = new(new StageDefinition(4));

        Assert.That(calculator.Calculate(beatmap), Is.Zero);
    }

    [Test]
    public void NonEmptyManiaBeatmapCalculatesStableFiniteValue()
    {
        SunnyManiaDifficultyCalculator calculator = new();
        ManiaBeatmap beatmap = new(new StageDefinition(4))
        {
            HitObjects =
            {
                new Note { StartTime = 0, Column = 0 },
                new Note { StartTime = 120, Column = 1 },
                new Note { StartTime = 240, Column = 0 },
                new Note { StartTime = 360, Column = 2 },
            },
        };

        double result = calculator.Calculate(beatmap);

        Assert.That(double.IsFinite(result), Is.True);
        Assert.That(result, Is.EqualTo(0.020137985827712358d).Within(1e-15));
    }

    [Test]
    public void NonManiaBeatmapThrowsArgumentException()
    {
        SunnyManiaDifficultyCalculator calculator = new();

        Assert.That(() => calculator.Calculate(new Beatmap()), Throws.ArgumentException);
    }

    [Test]
    public void GetStrainTimeline_TwoNotes_ReturnsOneEntry()
    {
        var beatmap = new ManiaBeatmap(new StageDefinition(4));
        beatmap.HitObjects.Add(new Note { StartTime = 0, Column = 0 });
        beatmap.HitObjects.Add(new Note { StartTime = 500, Column = 1 });
        var calc = new SunnyManiaDifficultyCalculator();

        (double Time, double Strain)[] timeline = calc.GetStrainTimeline(beatmap, Array.Empty<Mod>());

        // 첫 노트는 건너뜀(이전 노트 없음), 두 번째 노트부터 엔트리 생성
        Assert.That(timeline, Has.Length.EqualTo(1));
        Assert.That(timeline[0].Time, Is.GreaterThanOrEqualTo(0));
        Assert.That(timeline[0].Strain, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GetStrainTimeline_NonManiaBeatmap_ThrowsArgumentException()
    {
        var calc = new SunnyManiaDifficultyCalculator();
        Assert.That(() => calc.GetStrainTimeline(new Beatmap()), Throws.ArgumentException);
    }

    [Test]
    public void GetStrainTimeline_EmptyBeatmap_ReturnsEmpty()
    {
        var beatmap = new ManiaBeatmap(new StageDefinition(4));
        var calc = new SunnyManiaDifficultyCalculator();

        var timeline = calc.GetStrainTimeline(beatmap, Array.Empty<Mod>());

        Assert.That(timeline, Is.Empty);
    }
}
