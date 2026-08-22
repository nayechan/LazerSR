using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace LazerSR.Hook.PatternCopy;

/// <summary>newScreen이 보내온 명령 하나.</summary>
internal readonly record struct PatternCommand(PatternCommandKind Kind, int Id, int Column, double TimeMs, double DurationMs);

internal enum PatternCommandKind
{
    /// <summary>
    /// 시간 원점 동기화. <c>TimeMs</c>는 보낸 쪽의 현재 경과 시각.
    /// <b>매 틱 반복해서 온다</b> — 원점이 아직 없을 때만 쓴다.
    /// 캡처 시작과 모드 진입의 순서가 어느 쪽이든 상관없게 하려는 것이다.
    /// </summary>
    Sync,

    /// <summary>길이가 확정된 노트. <c>DurationMs</c>가 0이면 단노트.</summary>
    Note,

    /// <summary>끝을 아직 모르는 롱노트. 나중에 <see cref="Cut"/>으로 길이가 확정된다.</summary>
    OpenHold,

    /// <summary>앞서 <see cref="OpenHold"/>로 깔아둔 롱노트의 실제 길이.</summary>
    Cut,

    Stop,
}

/// <summary>
/// newScreen(파이프) → 패턴 복제 화면 사이의 명령 큐.
/// <para>
/// 파이프는 백그라운드 스레드에서 읽히고 <c>Playfield.Add</c>는 업데이트 스레드에서만 안전하므로,
/// 여기서 한 번 끊어주고 <see cref="PatternCopyPlayer"/>가 매 <c>Update</c>에서 비운다.
/// </para>
/// </summary>
internal static class PatternCopyBridge
{
    private static readonly ConcurrentQueue<PatternCommand> queue = new();

    /// <summary>패턴 복제 화면이 떠 있는 동안에만 명령을 받는다.</summary>
    public static volatile bool Active;

    public static void Clear()
    {
        while (queue.TryDequeue(out _)) { }
    }

    public static bool TryDequeue(out PatternCommand command) => queue.TryDequeue(out command);

    /// <summary>
    /// 파이프로 들어온 한 줄을 해석해 큐에 넣는다. <c>pc:</c>로 시작하지 않으면 우리 명령이 아니다.
    /// </summary>
    /// <returns>이 줄을 우리가 처리했는지.</returns>
    public static bool TryHandleLine(string line)
    {
        if (!line.StartsWith("pc:", StringComparison.Ordinal))
            return false;

        // 화면이 없을 때 들어온 명령은 버린다 — 쌓아두면 나중에 진입했을 때 과거 노트가 쏟아진다.
        if (!Active)
            return true;

        try
        {
            string[] p = line.Split(':');

            switch (p[1])
            {
                case "sync":
                    queue.Enqueue(new PatternCommand(PatternCommandKind.Sync, 0, 0, Num(p[2]), 0));
                    return true;

                case "note":
                    // pc:note:<id>:<col>:<hitMs>:<durMs>
                    queue.Enqueue(new PatternCommand(PatternCommandKind.Note, int.Parse(p[2]), int.Parse(p[3]), Num(p[4]), Num(p[5])));
                    return true;

                case "hold":
                    // pc:hold:<id>:<col>:<hitMs>
                    queue.Enqueue(new PatternCommand(PatternCommandKind.OpenHold, int.Parse(p[2]), int.Parse(p[3]), Num(p[4]), 0));
                    return true;

                case "cut":
                    // pc:cut:<id>:<durMs>
                    queue.Enqueue(new PatternCommand(PatternCommandKind.Cut, int.Parse(p[2]), 0, 0, Num(p[3])));
                    return true;

                case "stop":
                    queue.Enqueue(new PatternCommand(PatternCommandKind.Stop, 0, 0, 0, 0));
                    return true;
            }
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] PatternCopyBridge: bad line '{line}': {ex.Message}");
        }

        return true;
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
