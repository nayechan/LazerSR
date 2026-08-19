using System.IO;
using System.Text.Json;

namespace LazerSR.Launcher.Configuration;

public sealed class LauncherSettingsStore
{
    private const string settingsFileName = "settings.json";
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public LauncherSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerSR",
            settingsFileName))
    {
    }

    public LauncherSettingsStore(string settingsFilePath)
    {
        SettingsFilePath = settingsFilePath;
    }

    public string SettingsFilePath { get; }

    public LauncherSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
            return new LauncherSettings(null);

        try
        {
            string json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<LauncherSettings>(json, jsonOptions) ?? new LauncherSettings(null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            PreserveUnreadableSettingsFile();
            return new LauncherSettings(null);
        }
    }

    public void Save(LauncherSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsFilePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, jsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    private void PreserveUnreadableSettingsFile()
    {
        if (!File.Exists(SettingsFilePath))
            return;

        string preservedPath = GetPreservedSettingsPath();

        try { File.Move(SettingsFilePath, preservedPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string GetPreservedSettingsPath()
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        string candidate = $"{SettingsFilePath}.corrupt-{timestamp}";
        return File.Exists(candidate) ? $"{candidate}-{Guid.NewGuid():N}" : candidate;
    }
}
