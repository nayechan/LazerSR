using System;
using System.IO;

// 네임스페이스를 루트에 둔다 — LazerSR.Hook.Storage로 두면 osu.Framework의 Storage 타입과 이름이
// 충돌해 같은 어셈블리의 다른 파일에서 Storage가 네임스페이스로 먼저 해석된다.
namespace LazerSR.Hook;

/// <summary>
/// LazerSR의 개인 저장소. <b>경로 결정 + 폴더 보장 + 안전한 읽기/쓰기</b>가 전부이고,
/// 무엇을 어떤 형식으로 담을지는 각 기능이 소유한다.
/// <para>
/// 위치는 <c>%LocalAppData%\LazerSR\</c>다. 설치 경로는 관리자 권한이 필요하고 재설치 때 덮이므로
/// 사용자가 쌓은 데이터를 두지 않는다. Roaming이 아니라 Local인 이유는 이 데이터가 그 PC의
/// 비트맵 라이브러리에 종속되고 용량이 커질 수 있기 때문이다 (런처 설정은 지금대로 Roaming에 남는다).
/// </para>
/// <para>
/// <b>저장 실패는 절대 기능을 막지 않는다.</b> 읽기는 실패 시 <c>null</c>, 쓰기는 <c>false</c>를
/// 돌려줄 뿐 예외를 밖으로 내보내지 않는다.
/// </para>
/// </summary>
public static class LazerSrStorage
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LazerSR");

    /// <summary>기능별 하위 폴더. 없으면 만든다. 실패하면 빈 문자열이다.</summary>
    public static string GetFolder(string name)
    {
        try
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);

            return path;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] LazerSrStorage.GetFolder({name}) failed: {e}");

            return string.Empty;
        }
    }

    /// <summary>파일이 없거나 읽을 수 없으면 <c>null</c>.</summary>
    public static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] LazerSrStorage.ReadText({path}) failed: {e}");

            return null;
        }
    }

    /// <summary>
    /// 임시 파일에 먼저 쓰고 교체한다 — 쓰는 도중 osu!가 죽어도 기존 파일이 반쪽으로 남지 않는다.
    /// </summary>
    public static bool WriteText(string path, string contents)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporary = path + ".tmp";

            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, true);

            return true;
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] LazerSrStorage.WriteText({path}) failed: {e}");

            return false;
        }
    }
}
