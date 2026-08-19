using LazerSR.Hook.Patches;
using NUnit.Framework;

namespace LazerSR.Hook.Tests;

[TestFixture]
public sealed class DanCalculatorTests
{
    // T[0]=3.3, T[1]=3.7 → lower[0]=3.3-(3.7-3.3)/2=3.1, upper[0]=(3.3+3.7)/2=3.5
    // t at 3.1 = 0     → low
    // t at 3.3 = 0.5   → mid
    // t at 3.4 = 0.75  → high
    // 3.5 → 2nd: lower[1]=3.5, t=0 → low

    [Test]
    public void BelowFirstBoundary_Returns1stLow()
    {
        var r = DanCalculator.GetDanInfo(3.0);
        Assert.That(r.Name, Is.EqualTo("1st"));
        Assert.That(r.Tier, Is.EqualTo("low"));
    }

    [Test]
    public void AtFirstBLeft_Returns1stLow()
    {
        var r = DanCalculator.GetDanInfo(3.1);
        Assert.That(r.Name, Is.EqualTo("1st"));
        Assert.That(r.Tier, Is.EqualTo("low"));
    }

    [Test]
    public void AtThreshold_Returns1stMid()
    {
        // T[0]=3.3, t=(3.3-3.1)/0.4=0.5 → mid
        var r = DanCalculator.GetDanInfo(3.3);
        Assert.That(r.Name, Is.EqualTo("1st"));
        Assert.That(r.Tier, Is.EqualTo("mid"));
    }

    [Test]
    public void UpperHalfOf1st_Returns1stHigh()
    {
        // t=(3.4-3.1)/0.4=0.75 → high
        var r = DanCalculator.GetDanInfo(3.4);
        Assert.That(r.Name, Is.EqualTo("1st"));
        Assert.That(r.Tier, Is.EqualTo("high"));
    }

    [Test]
    public void BoundaryBetween1stAnd2nd_Returns2ndLow()
    {
        // upper[0]=3.5 → 3.5 falls into 2nd; lower[1]=3.5, t=0 → low
        var r = DanCalculator.GetDanInfo(3.5);
        Assert.That(r.Name, Is.EqualTo("2nd"));
        Assert.That(r.Tier, Is.EqualTo("low"));
    }

    [Test]
    public void LastDanAboveBRight_ReturnsLastDanHigh()
    {
        // Theta is the last dan. Any value >= upper[19] returns Theta high.
        var r = DanCalculator.GetDanInfo(11.5);
        Assert.That(r.Name, Is.EqualTo("Theta"));
        Assert.That(r.Tier, Is.EqualTo("high"));
    }
}
