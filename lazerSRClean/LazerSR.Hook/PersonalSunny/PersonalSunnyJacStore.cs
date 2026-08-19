using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LazerSR.Hook.PersonalSunny;

/// <summary>
/// Baked Jacobians, keyed by <see cref="PersonalSunnyJacKey"/>. <c>%LocalAppData%\LazerSR\personalsunny\jac_cache.json</c>.
/// Separate file from the queue on purpose - a corrupt cache shouldn't take the queue down with it, and
/// it can always be rebaked; the queue can't be reconstructed once scores scroll out of local history.
/// Pruned to only the keys the current queue still references, so it never grows past what's in use.
/// </summary>
public static class PersonalSunnyJacStore
{
    private const int schema_version = 1;
    private const string folder_name = "personalsunny";
    private const string file_name = "jac_cache.json";

    private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry>? cache;

    private static Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry> Cache => cache ??= load();

    public static bool TryGet(PersonalSunnyJacKey key, out PersonalSunnyJacEntry entry) => Cache.TryGetValue(key, out entry!);

    public static void Put(PersonalSunnyJacEntry entry)
    {
        Cache[entry.Key] = entry;
        save();
    }

    /// <summary>Drops cached entries no queued item references any more.</summary>
    public static void PruneTo(IEnumerable<PersonalSunnyJacKey> keysStillInUse)
    {
        var keep = new HashSet<PersonalSunnyJacKey>(keysStillInUse);
        var stale = Cache.Keys.Where(k => !keep.Contains(k)).ToList();

        if (stale.Count == 0)
            return;

        foreach (var key in stale)
            Cache.Remove(key);

        save();
    }

    private static string filePath()
    {
        string folder = LazerSrStorage.GetFolder(folder_name);
        return string.IsNullOrEmpty(folder) ? string.Empty : Path.Combine(folder, file_name);
    }

    private static Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry> load()
    {
        string path = filePath();
        if (string.IsNullOrEmpty(path))
            return new Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry>();

        string? text = LazerSrStorage.ReadText(path);
        if (string.IsNullOrEmpty(text))
            return new Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry>();

        try
        {
            var file = JsonSerializer.Deserialize<JacFile>(text, json_options);

            if (file == null || file.Version != schema_version || file.Entries == null)
                return new Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry>();

            var result = new Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry>();

            // A bad entry (wrong jacobian length, say) is dropped, not fatal to the rest.
            foreach (var entry in file.Entries)
            {
                if (!string.IsNullOrEmpty(entry.BeatmapMd5) && entry.Jacobian is { Length: > 0 })
                    result[entry.Key] = entry;
            }

            return result;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyJacStore load failed: {e}");
            return new Dictionary<PersonalSunnyJacKey, PersonalSunnyJacEntry>();
        }
    }

    private static void save()
    {
        string path = filePath();
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            string text = JsonSerializer.Serialize(new JacFile(schema_version, Cache.Values.ToList()), json_options);
            LazerSrStorage.WriteText(path, text);
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyJacStore save failed: {e}");
        }
    }

    private class JacFile
    {
        public JacFile()
        {
        }

        public JacFile(int version, List<PersonalSunnyJacEntry>? entries)
        {
            Version = version;
            Entries = entries;
        }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("entries")]
        public List<PersonalSunnyJacEntry>? Entries { get; set; }
    }
}
