using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ColorShade.Recovery;

internal sealed class GuardianClient : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal GuardianClient()
    {
        Name = "ColorShade-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
    private string Name { get; }
    internal async Task<Reply> StartAsync()
    {
        AppPaths.LaunchSelf("--guardian", Name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await _pipe.WaitForConnectionAsync(timeout.Token);
        _reader = new StreamReader(_pipe, new UTF8Encoding(false), false, 4096, true);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        return await ReadAsync(timeout.Token);
    }
    internal async Task<Reply> SendAsync(Command command)
    {
        await _gate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await _writer!.WriteLineAsync(JsonSerializer.Serialize(command).AsMemory(), timeout.Token);
            return await ReadAsync(timeout.Token);
        }
        finally { _gate.Release(); }
    }
    private async Task<Reply> ReadAsync(CancellationToken cancellation)
    {
        var line = await _reader!.ReadLineAsync(cancellation) ?? throw new IOException("The recovery helper disconnected.");
        return JsonSerializer.Deserialize<Reply>(line) ?? throw new IOException("Invalid recovery helper reply.");
    }
    public void Dispose()
    {
        // Closing the pipe is also a recovery signal. Do not kill the child: it must restore.
        _pipe.Dispose(); _reader?.Dispose(); _writer?.Dispose();
    }
}
