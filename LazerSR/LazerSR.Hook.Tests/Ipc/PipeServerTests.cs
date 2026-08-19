using System.IO.Pipes;
using LazerSR.Hook;
using LazerSR.Hook.Ipc;
using NUnit.Framework;

namespace LazerSR.Hook.Tests.Ipc;

[TestFixture]
public sealed class PipeServerTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Start the server once for all tests in this fixture
        PipeServer.StartBackground();
    }

    [SetUp]
    public void SetUp()
    {
        SunnyState.ResetForTesting();
    }

    [Test]
    public async Task Connect_SendsConnectedLine()
    {
        using var client = await ConnectAsync();

        string? firstLine = await client.Reader.ReadLineAsync();
        Assert.That(firstLine, Is.EqualTo("connected"));
    }

    [Test]
    public async Task SunnyOn_SetsEnabledTrue()
    {
        using var client = await ConnectAsync();
        await client.Reader.ReadLineAsync(); // consume "connected"

        await client.Writer.WriteLineAsync("sunny:on");
        await client.Writer.FlushAsync();

        await PollAsync(() => SunnyState.Enabled, expectedValue: true);
        Assert.That(SunnyState.Enabled, Is.True);
    }

    [Test]
    public async Task SunnyOff_SetsEnabledFalse()
    {
        SunnyState.SetEnabled(true);

        using var client = await ConnectAsync();
        await client.Reader.ReadLineAsync(); // consume "connected"

        await client.Writer.WriteLineAsync("sunny:off");
        await client.Writer.FlushAsync();

        await PollAsync(() => !SunnyState.Enabled, expectedValue: true);
        Assert.That(SunnyState.Enabled, Is.False);
    }

    [Test]
    public async Task UnknownCommand_IsIgnoredWithoutException()
    {
        using var client = await ConnectAsync();
        await client.Reader.ReadLineAsync(); // consume "connected"

        // Send unknown command — server should stay alive
        await client.Writer.WriteLineAsync("unknown:command");
        await client.Writer.FlushAsync();

        // Follow up with a known command to confirm the connection is still alive
        await client.Writer.WriteLineAsync("sunny:on");
        await client.Writer.FlushAsync();

        await PollAsync(() => SunnyState.Enabled, expectedValue: true);
        Assert.That(SunnyState.Enabled, Is.True);
    }

    // --- helpers ---

    private static async Task<PipeConnection> ConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", PipeServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout: 5000);
        var reader = new StreamReader(pipe);
        var writer = new StreamWriter(pipe) { AutoFlush = false };
        return new PipeConnection(pipe, reader, writer);
    }

    private static async Task PollAsync(Func<bool> condition, bool expectedValue, int timeoutMs = 3000, int intervalMs = 20)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(intervalMs);
        }
    }

    private sealed class PipeConnection : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        public StreamReader Reader { get; }
        public StreamWriter Writer { get; }

        public PipeConnection(NamedPipeClientStream pipe, StreamReader reader, StreamWriter writer)
        {
            _pipe = pipe;
            Reader = reader;
            Writer = writer;
        }

        public void Dispose() => _pipe.Dispose();
    }
}

