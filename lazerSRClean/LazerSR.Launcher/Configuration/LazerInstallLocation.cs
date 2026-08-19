using System.IO;

namespace LazerSR.Launcher.Configuration;

public sealed record LazerInstallLocation(string? Path)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Path);

    public bool IsValid => IsConfigured && Directory.Exists(Path);
}
