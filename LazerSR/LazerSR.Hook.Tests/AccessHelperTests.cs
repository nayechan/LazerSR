using LazerSR.Hook;
using NUnit.Framework;

namespace LazerSR.Hook.Tests;

[TestFixture]
public sealed class AccessHelperTests
{
    [Test]
    public void TryGet_FindsPublicProperty()
    {
        var instance = new Sample { PublicProp = 42 };

        bool ok = AccessHelper.TryGet<int>(typeof(Sample), nameof(Sample.PublicProp), instance, out var value);

        Assert.That(ok, Is.True);
        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void TryGet_FindsPrivateField()
    {
        var instance = new Sample();
        instance.SetPrivateField("hello");

        bool ok = AccessHelper.TryGet<string>(typeof(Sample), "privateField", instance, out var value);

        Assert.That(ok, Is.True);
        Assert.That(value, Is.EqualTo("hello"));
    }

    [Test]
    public void TryGet_FindsPrivateAutoProperty()
    {
        // Note: AccessTools.Property finds private auto-properties directly, so this exercises
        // the Property path (not the backing-field path). The backing-field branch in AccessHelper
        // is a safety net for unusual cases (e.g. internal compiler-generated types from other
        // assemblies) where the property lookup fails but the synthesized `<name>k__BackingField`
        // is still reachable as a field.
        var instance = new Sample();
        instance.SetPrivateAutoProp(7.5);

        bool ok = AccessHelper.TryGet<double>(typeof(Sample), "privateAutoProp", instance, out var value);

        Assert.That(ok, Is.True);
        Assert.That(value, Is.EqualTo(7.5));
    }

    [Test]
    public void TryGet_FindsPublicField_WhenNoPropertyExists()
    {
        var instance = new HasFieldOnly();

        bool ok = AccessHelper.TryGet<int>(typeof(HasFieldOnly), "myField", instance, out var value);

        Assert.That(ok, Is.True);
        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void TryGet_ReturnsFalseForNonexistentMember()
    {
        var instance = new Sample();

        bool ok = AccessHelper.TryGet<int>(typeof(Sample), "thisDoesNotExist", instance, out _);

        Assert.That(ok, Is.False);
    }

    private sealed class Sample
    {
        public int PublicProp { get; set; }

        private string privateField = string.Empty;

        private double privateAutoProp { get; set; }

        public void SetPrivateField(string value) => privateField = value;
        public void SetPrivateAutoProp(double value) => privateAutoProp = value;
    }

    private sealed class HasFieldOnly
    {
        public int myField = 42;
    }
}
