using LazerSR.Launcher.Configuration;
using NUnit.Framework;

namespace LazerSR.Hook.Tests;

[TestFixture]
public sealed class LauncherSettingsStoreTests
{
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "LazerSR.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }

    [Test]
    public void LoadReturnsDefaultSettingsWhenSettingsFileDoesNotExist()
    {
        LauncherSettingsStore store = new(Path.Combine(tempDirectory, "settings.json"));

        LauncherSettings settings = store.Load();

        Assert.That(settings.OsuLazerInstallPath, Is.Null);
    }

    [Test]
    public void SaveAndLoadPersistSelectedInstallPath()
    {
        string settingsPath = Path.Combine(tempDirectory, "settings.json");
        string installPath = Path.Combine(tempDirectory, "selected-install");
        LauncherSettingsStore store = new(settingsPath);

        store.Save(new LauncherSettings(installPath));
        LauncherSettings loaded = store.Load();

        Assert.That(loaded.OsuLazerInstallPath, Is.EqualTo(installPath));
    }

    [Test]
    public void LoadReturnsDefaultSettingsAndPreservesFileWhenSettingsJsonIsCorrupt()
    {
        string settingsPath = Path.Combine(tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, "{ not valid json");
        LauncherSettingsStore store = new(settingsPath);

        LauncherSettings settings = store.Load();

        Assert.That(settings.OsuLazerInstallPath, Is.Null);
        Assert.That(File.Exists(settingsPath), Is.False);
        Assert.That(Directory.GetFiles(tempDirectory, "settings.json.corrupt-*"), Has.Length.EqualTo(1));
    }

    [Test]
    public void InstallPathProviderLoadsStoredPathIntoCurrentLocation()
    {
        string settingsPath = Path.Combine(tempDirectory, "settings.json");
        string installPath = Path.Combine(tempDirectory, "selected-install");
        Directory.CreateDirectory(installPath);
        LauncherSettingsStore store = new(settingsPath);
        store.Save(new LauncherSettings(installPath));
        InstallPathProvider provider = new(store);

        LazerInstallLocation location = provider.Load();

        Assert.That(location.Path, Is.EqualTo(installPath));
        Assert.That(provider.Current.Path, Is.EqualTo(installPath));
        Assert.That(location.IsValid, Is.True);
    }
}
