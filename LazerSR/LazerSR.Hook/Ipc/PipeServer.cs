using System.Diagnostics;
using System.IO.Pipes;

namespace LazerSR.Hook.Ipc;

public static class PipeServer
{
    private static int _started;

    public static string PipeName => $"osu-lazer-sr-mod-{Environment.ProcessId}";

    public static void StartBackground()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            Task.Run(RunLoop);
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
                    Trace.WriteLine($"[PipeServer] WaitForConnectionAsync failed: {ex.Message}");
                    await pipe.DisposeAsync();
                    continue;
                }

                _ = Task.Run(() => HandleConnectionAsync(pipe));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PipeServer] RunLoop error: {ex.Message}");
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
                // unknown lines (incl. legacy feature:* commands) are silently ignored.
            }
        }
    }
}
