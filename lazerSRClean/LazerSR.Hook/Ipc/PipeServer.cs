using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace LazerSR.Hook.Ipc;

public static class PipeServer
{
    private static int _started;
    private static StreamWriter? _activeWriter;
    private static readonly object _writerLock = new();

    public static string PipeName => $"osu-lazer-sr-mod-{Environment.ProcessId}";

    public static void StartBackground()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            Task.Run(RunLoop);
    }

    public static async Task BroadcastAsync(string message)
    {
        StreamWriter? writer;
        lock (_writerLock)
            writer = _activeWriter;
        if (writer == null) return;
        try
        {
            await writer.WriteLineAsync(message).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[PipeServer] BroadcastAsync failed: {ex.Message}");
        }
    }

    private static async Task RunLoop()
    {
        while (true)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await pipe.WaitForConnectionAsync();
                }
                catch (Exception ex)
                {
                    HookLog.Write($"[PipeServer] WaitForConnectionAsync failed: {ex.Message}");
                    await pipe.DisposeAsync();
                    continue;
                }

                _ = Task.Run(() => HandleConnectionAsync(pipe));
            }
            catch (Exception ex)
            {
                HookLog.Write($"[PipeServer] RunLoop error: {ex.Message}");
            }
        }
    }

    private static async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = false };
            using var _ = writer;

            lock (_writerLock)
                _activeWriter = writer;

            try
            {
                await writer.WriteAsync("connected\n");
                await writer.FlushAsync();

                while (pipe.IsConnected)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync();
                    }
                    catch
                    {
                        break;
                    }

                    if (line is null)
                        break;

                    if (line == "sunny:on")
                        SunnyState.SetEnabled(true);
                    else if (line == "sunny:off")
                        SunnyState.SetEnabled(false);
                }
            }
            finally
            {
                lock (_writerLock)
                {
                    if (ReferenceEquals(_activeWriter, writer))
                        _activeWriter = null;
                }
            }
        }
    }
}
