using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LazerSR.Hook.Training;

/// <summary>
/// 사용자가 등록한 배경음 목록. <c>%LocalAppData%\LazerSR\music\songs.json</c> 하나에 담긴다.
/// <para>
/// <b>오디오는 복사하지 않고 참조만 한다.</b> 저장하는 것은 비트맵 식별자(MD5)와 오디오 파일 이름이고,
/// 실제 경로는 재생 직전에 realm으로 다시 해결한다 — 절대 경로를 저장하면 osu! 데이터 폴더를
/// 옮기는 순간 전부 깨진다. 맵이 지워져 못 찾으면 그 곡만 조용히 건너뛴다.
/// </para>
/// </summary>
public static class TrainingMusicStore
{
    /// <summary>스키마 버전. 모르는 버전이면 조용히 빈 목록에서 시작한다 — 다시 등록하면 그만이다.</summary>
    private const int schema_version = 2;

    private const string folder_name = "music";
    private const string file_name = "songs.json";

    private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static List<MusicTrackInfo>? songs;

    /// <summary>목록이 바뀔 때마다 발화. 곡 추가 UI가 표시를 갱신하는 데 쓴다.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<MusicTrackInfo> Songs => songs ??= load();

    /// <summary>
    /// 같은 오디오가 이미 등록됐는가. <b>판정은 오디오 파일 해시로 한다</b> —
    /// 검사는 난이도 단위지만 오디오는 맵셋 공용이라, 같은 셋의 다른 난이도가 같은 곡을 또 데려온다.
    /// </summary>
    public static bool Contains(string audioHash) =>
        !string.IsNullOrEmpty(audioHash) && Songs.Any(s => s.AudioHash == audioHash);

    /// <summary>이미 있는 곡이면 아무 일도 하지 않고 <c>false</c>.</summary>
    public static bool Add(MusicTrackInfo song)
    {
        if (Contains(song.AudioHash))
            return false;

        var list = new List<MusicTrackInfo>(Songs) { song };

        songs = list;
        save();
        Changed?.Invoke();

        return true;
    }

    /// <summary>목록에서 뺀다. 오디오 파일에는 손대지 않는다 — 우리는 참조만 하고 있었다.</summary>
    public static void Remove(MusicTrackInfo song)
    {
        var list = new List<MusicTrackInfo>(Songs);

        if (list.RemoveAll(s => s.AudioHash == song.AudioHash) == 0)
            return;

        songs = list;
        save();
        Changed?.Invoke();
    }

    /// <summary>훈련에서 이 곡을 쓸지 바꾼다.</summary>
    public static void SetEnabled(MusicTrackInfo song, bool enabled)
    {
        var list = new List<MusicTrackInfo>(Songs);
        int index = list.FindIndex(s => s.AudioHash == song.AudioHash);

        if (index < 0 || list[index].Enabled == enabled)
            return;

        list[index] = list[index] with { Enabled = enabled };

        songs = list;
        save();
        Changed?.Invoke();
    }

    private static string filePath()
    {
        string folder = LazerSrStorage.GetFolder(folder_name);

        return string.IsNullOrEmpty(folder) ? string.Empty : Path.Combine(folder, file_name);
    }

    private static List<MusicTrackInfo> load()
    {
        string path = filePath();

        if (string.IsNullOrEmpty(path))
            return new List<MusicTrackInfo>();

        string? text = LazerSrStorage.ReadText(path);

        if (string.IsNullOrEmpty(text))
            return new List<MusicTrackInfo>();

        try
        {
            var file = JsonSerializer.Deserialize<SongFile>(text, json_options);

            if (file == null || file.Version != schema_version || file.Songs == null)
                return new List<MusicTrackInfo>();

            var result = new List<MusicTrackInfo>();

            // 값이 망가진 항목은 조용히 버린다 — 남은 곡으로 계속 돌아간다.
            foreach (var song in file.Songs)
            {
                if (song.Bpm > 0 && song.BaseBpm > 0 && !string.IsNullOrEmpty(song.BeatmapMd5))
                    result.Add(song);
            }

            return result;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] TrainingMusicStore load failed: {e}");

            return new List<MusicTrackInfo>();
        }
    }

    private static void save()
    {
        string path = filePath();

        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            string text = JsonSerializer.Serialize(new SongFile(schema_version, songs), json_options);
            LazerSrStorage.WriteText(path, text);
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] TrainingMusicStore save failed: {e}");
        }
    }

    private class SongFile
    {
        public SongFile()
        {
        }

        public SongFile(int version, List<MusicTrackInfo>? songs)
        {
            Version = version;
            Songs = songs;
        }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("songs")]
        public List<MusicTrackInfo>? Songs { get; set; }
    }
}
