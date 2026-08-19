using LazerSR.Hook;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;

namespace LazerSR.Hook.Tests.Sunny;

[TestFixture]
public sealed class SunnyCalculatorRunnerTests
{
    [Test]
    public void EmptyManiaBeatmapReturnsZero()
    {
        var beatmap = new ManiaBeatmap(new StageDefinition(4));
        beatmap.BeatmapInfo.Difficulty = new BeatmapDifficulty
        {
            CircleSize = 4,
            OverallDifficulty = 7,
        };

        double result = SunnyCalculatorRunner.Calculate(beatmap, Array.Empty<Mod>());

        Assert.That(result, Is.Zero);
    }

    [Test]
    public void DensePatternReturnsHigherSRThanSparsePattern()
    {
        var denseBeatmap = new ManiaBeatmap(new StageDefinition(4));
        denseBeatmap.BeatmapInfo.Difficulty = new BeatmapDifficulty
        {
            CircleSize = 4,
            OverallDifficulty = 7,
        };
        // Dense: notes every 10ms
        for (int i = 0; i < 50; i++)
            denseBeatmap.HitObjects.Add(new Note { StartTime = i * 10, Column = i % 4 });

        var sparseBeatmap = new ManiaBeatmap(new StageDefinition(4));
        sparseBeatmap.BeatmapInfo.Difficulty = new BeatmapDifficulty
        {
            CircleSize = 4,
            OverallDifficulty = 7,
        };
        // Sparse: notes every 1000ms
        for (int i = 0; i < 50; i++)
            sparseBeatmap.HitObjects.Add(new Note { StartTime = i * 1000, Column = i % 4 });

        double denseResult = SunnyCalculatorRunner.Calculate(denseBeatmap, Array.Empty<Mod>());
        double sparseResult = SunnyCalculatorRunner.Calculate(sparseBeatmap, Array.Empty<Mod>());

        Assert.That(denseResult, Is.GreaterThan(sparseResult));
    }

    [Test]
    public void NonManiaBeatmapReturnsZero()
    {
        double result = SunnyCalculatorRunner.Calculate(new Beatmap(), Array.Empty<Mod>());

        Assert.That(result, Is.Zero);
    }

    [Test]
    public void CancelledTokenThrowsOperationCanceledException()
    {
        var beatmap = new ManiaBeatmap(new StageDefinition(4));
        beatmap.BeatmapInfo.Difficulty = new BeatmapDifficulty
        {
            CircleSize = 4,
            OverallDifficulty = 7,
        };
        for (int i = 0; i < 100; i++)
            beatmap.HitObjects.Add(new Note { StartTime = i * 10, Column = i % 4 });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            () => SunnyCalculatorRunner.Calculate(beatmap, Array.Empty<Mod>(), cts.Token),
            Throws.InstanceOf<OperationCanceledException>()
        );
    }
}
