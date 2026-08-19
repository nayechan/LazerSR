using LazerSR.Hook.Calculators;
using NUnit.Framework;

namespace LazerSR.Hook.Tests.Calculators;

[TestFixture]
public sealed class SunnyStrainGraphTests
{
    [Test]
    public void SmoothD_EmptyInput_ReturnsEmptyArrays()
    {
        var (times, strains) = SunnyStrainGraph.SmoothD(Array.Empty<(double, double)>());
        Assert.That(times, Is.Empty);
        Assert.That(strains, Is.Empty);
    }

    [Test]
    public void SmoothD_SinglePoint_ReturnsSinglePoint()
    {
        var (times, strains) = SunnyStrainGraph.SmoothD(new[] { (1000.0, 5.0) });
        Assert.That(times, Has.Length.EqualTo(1));
        Assert.That(strains[0], Is.GreaterThan(0));
        Assert.That(strains[0], Is.LessThan(5.0));
    }

    [Test]
    public void SmoothD_ConstantStrain_IsSmoothedWithZeroPaddedEdges()
    {
        var raw = Enumerable.Range(0, 20)
            .Select(i => ((double)(i * 100), 4.0))
            .ToArray();
        var (times, strains) = SunnyStrainGraph.SmoothD(raw);
        Assert.That(strains[0], Is.LessThan(strains[times.Length / 2]));
        Assert.That(strains[^1], Is.LessThan(strains[times.Length / 2]));
        Assert.That(strains, Is.All.GreaterThan(0));
    }

    [Test]
    public void SmoothD_TimesAre100msApart()
    {
        var raw = Enumerable.Range(0, 50)
            .Select(i => ((double)(i * 100), (double)i))
            .ToArray();
        var (times, _) = SunnyStrainGraph.SmoothD(raw);
        for (int i = 1; i < times.Length; i++)
            Assert.That(times[i] - times[i - 1], Is.EqualTo(100.0).Within(0.01));
    }

    [Test]
    public void FindHoneySpots_EmptyStrains_ReturnsEmpty()
    {
        var result = SunnyStrainGraph.FindHoneySpots(Array.Empty<(double, double)>());
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindHoneySpots_AllZero_ReturnsEmpty()
    {
        var raw = Enumerable.Range(0, 100).Select(i => ((double)(i * 100), 0.0)).ToArray();
        var result = SunnyStrainGraph.FindHoneySpots(raw);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindHoneySpots_OneHighPeak_ReturnsOneSpot()
    {
        var raw = Enumerable.Range(0, 80)
            .Select(i =>
            {
                double strain = i is >= 30 and <= 45 ? 10.0 : 1.0;
                return ((double)(i * 400), strain);
            })
            .ToArray();

        var result = SunnyStrainGraph.FindHoneySpots(raw);
        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Min(), Is.LessThanOrEqualTo(31 * 400));
        Assert.That(result.Max(), Is.GreaterThanOrEqualTo(45 * 400));
    }
}
