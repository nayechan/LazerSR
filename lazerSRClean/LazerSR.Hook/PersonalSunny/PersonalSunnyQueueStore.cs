using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LazerSR.Hook.PersonalSunny;

/// <summary>
/// The queued scores a personal-diff fit runs against. <c>%LocalAppData%\LazerSR\personalsunny\queue.json</c>.
/// Capped at <see cref="MaxEntries"/>, FIFO - the oldest entry drops when a new one would overflow it.
/// The same map can appear more than once (a player naturally replays maps); no dedup.
/// </summary>
public static class PersonalSunnyQueueStore
{
    public const int MaxEntries = 100;

    private const int schema_version = 1;
    private const string folder_name = "personalsunny";
    private const string file_name = "queue.json";

    private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static List<PersonalSunnyQueueEntry>? entries;

    /// <summary>Fires whenever the queue changes (append, replace, or reset).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<PersonalSunnyQueueEntry> Entries => entries ??= load();

    /// <summary>Appends one entry, dropping the oldest if this overflows <see cref="MaxEntries"/>.</summary>
    public static void Add(PersonalSunnyQueueEntry entry)
    {
        var list = new List<PersonalSunnyQueueEntry>(Entries) { entry };

        while (list.Count > MaxEntries)
            list.RemoveAt(0);

        entries = list;
        save();
        Changed?.Invoke();
    }

    /// <summary>Replaces the whole queue - used by a bulk re-collect from realm, which is the authoritative source.</summary>
    public static void ReplaceAll(IReadOnlyList<PersonalSunnyQueueEntry> newEntries)
    {
        var list = new List<PersonalSunnyQueueEntry>(newEntries);

        while (list.Count > MaxEntries)
            list.RemoveAt(0);

        entries = list;
        save();
        Changed?.Invoke();
    }

    private static string filePath()
    {
        string folder = LazerSrStorage.GetFolder(folder_name);
        return string.IsNullOrEmpty(folder) ? string.Empty : Path.Combine(folder, file_name);
    }

    private static List<PersonalSunnyQueueEntry> load()
    {
        string path = filePath();
        if (string.IsNullOrEmpty(path))
            return new List<PersonalSunnyQueueEntry>();

        string? text = LazerSrStorage.ReadText(path);
        if (string.IsNullOrEmpty(text))
            return new List<PersonalSunnyQueueEntry>();

        try
        {
            var file = JsonSerializer.Deserialize<QueueFile>(text, json_options);

            if (file == null || file.Version != schema_version || file.Entries == null)
                return new List<PersonalSunnyQueueEntry>();

            return file.Entries;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyQueueStore load failed: {e}");
            return new List<PersonalSunnyQueueEntry>();
        }
    }

    private static void save()
    {
        string path = filePath();
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            string text = JsonSerializer.Serialize(new QueueFile(schema_version, entries), json_options);
            LazerSrStorage.WriteText(path, text);
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] PersonalSunnyQueueStore save failed: {e}");
        }
    }

    private class QueueFile
    {
        public QueueFile()
        {
        }

        public QueueFile(int version, List<PersonalSunnyQueueEntry>? entries)
        {
            Version = version;
            Entries = entries;
        }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("entries")]
        public List<PersonalSunnyQueueEntry>? Entries { get; set; }
    }
}
