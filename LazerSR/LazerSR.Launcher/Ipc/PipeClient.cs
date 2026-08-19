using System.IO;
using System.IO.Pipes;

namespace LazerSR.Launcher.Ipc;

public enum PipeStatus { Disconnected, Connecting, Connected }

public sealed class PipeClient : IDisposable
{
    private readonly string pipeName;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private NamedPipeClientStream? pipe;
    private StreamWriter? writer;
    private readonly object syncLock = new();

    public PipeClient(int osuPid)
    {
        pipeName = $"osu-lazer-sr-mod-{osuPid}";
    }

    public PipeStatus Status { get; private set; } = PipeStatus.Disconnected;
    public event Action<PipeStatus>? StatusChanged;

    public void ConnectAsync()
    {
        lock (syncLock)
        {
            if (Status == PipeStatus.Connecting || Status == PipeStatus.Connected)
                return;

            Status = PipeStatus.Connecting;
        }

        StatusChanged?.Invoke(PipeStatus.Connecting);

        _ = Task.Run(ConnectCore).ContinueWith(
            _ => HandleDisconnect(),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public async Task SendAsync(string command)
    {
        await SendLineAsync(command).ConfigureAwait(false);
    }

    public void Dispose()
    {
        PipeStatus prev;
        lock (syncLock)
        {
            writer?.Dispose();
            writer = null;
            pipe?.Dispose();
            pipe = null;
            prev = Status;
            Status = PipeStatus.Disconnected;
        }

        if (prev != PipeStatus.Disconnected)
            StatusChanged?.Invoke(PipeStatus.Disconnected);
    }

    private async Task ConnectCore()
    {
        var newPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await newPipe.ConnectAsync(30_000).ConfigureAwait(false);
        }
        catch (Exception)
        {
            newPipe.Dispose();
            HandleDisconnect();
            return;
        }

        var newWriter = new StreamWriter(newPipe, leaveOpen: true) { AutoFlush = false };
        var reader = new StreamReader(newPipe, leaveOpen: true);

        lock (syncLock)
        {
            if (Status == PipeStatus.Disconnected && pipe == null && writer == null)
            {
                newWriter.Dispose();
                newPipe.Dispose();
                return;
            }

            pipe = newPipe;
            writer = newWriter;
        }

        string? firstLine;
        try
        {
            firstLine = await reader.ReadLineAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            reader.Dispose();
            HandleDisconnect();
            return;
        }

        reader.Dispose();

        if (firstLine?.Trim() == "connected")
        {
            lock (syncLock) { Status = PipeStatus.Connected; }
            StatusChanged?.Invoke(PipeStatus.Connected);
        }
        else
            HandleDisconnect();
    }

    private async Task SendLineAsync(string command)
    {
        StreamWriter? w;
        lock (syncLock)
        {
            if (Status != PipeStatus.Connected)
                return;
            w = writer;
        }

        if (w is null)
            return;

        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await w.WriteLineAsync(command).ConfigureAwait(false);
            await w.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            HandleDisconnect();
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void HandleDisconnect()
    {
        lock (syncLock)
        {
            writer?.Dispose();
            writer = null;
            pipe?.Dispose();
            pipe = null;
            Status = PipeStatus.Disconnected;
        }

        StatusChanged?.Invoke(PipeStatus.Disconnected);
    }
}
