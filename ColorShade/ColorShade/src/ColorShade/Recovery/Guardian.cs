using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ColorShade.Native;

namespace ColorShade.Recovery;

internal static class Guardian
{
    private static int _sessionBlocked, _suspended;
    internal static async Task<int> RunAsync(string? pipeName)
    {
        Directory.CreateDirectory(AppPaths.Data);
        FileStream ownership;
        try
        {
            // OS releases this exclusive file handle even after forced termination.
            // An older guardian must finish recovery before another can write displays.
            ownership = new FileStream(Path.Combine(AppPaths.Data, "display-owner.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { AppPaths.Log("Recovery owner already exists; no display writes attempted."); return 3; }
        using (ownership)
        using (var system = new WindowsDisplaySystem())
        {
            DisplayEngine? engine = null;
            try
            {
                engine = new DisplayEngine(system, new JournalStore(AppPaths.Journal));
                AppPaths.Log("Guardian started; PID " + Environment.ProcessId);
                if (pipeName is null)
                {
                    await RecoverAsync(engine);
                    return engine.Pending ? 2 : 0;
                }
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(10_000);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                await Send(writer, engine.Update(new Command())); // Recover before accepting any non-neutral request.
                SystemEvents.SessionSwitch += SessionChanged;
                SystemEvents.PowerModeChanged += PowerChanged;
                bool previewWasRequested = false;
                long previewDeadline = 0;
                while (pipe.IsConnected)
                {
                    using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    string? line = await reader.ReadLineAsync(lease.Token);
                    if (line is null) break;
                    if (line.Length > 4096) throw new InvalidDataException("Oversized display command.");
                    var command = JsonSerializer.Deserialize<Command>(line)
                        ?? throw new InvalidDataException("Missing display command.");
                    if (command.Vibrance is < 0 or > 100 || command.Brightness is < 0 or > 100
                        || command.TargetDevice?.Length > 64) throw new InvalidDataException("Invalid display command.");
                    if (command.Preview && !previewWasRequested) previewDeadline = Environment.TickCount64 + 10_000;
                    previewWasRequested = command.Preview;
                    if (command.Stop || Volatile.Read(ref _sessionBlocked) != 0 || Volatile.Read(ref _suspended) != 0
                        || (command.Preview && Environment.TickCount64 >= previewDeadline))
                        command = command with { TargetDevice = null };
                    Reply reply;
                    try { reply = engine.Update(command); }
                    catch (Exception ex)
                    {
                        AppPaths.Log("Display command failed: " + ex.Message);
                        try { engine.Restore(); } catch (Exception restoreError) { AppPaths.Log(restoreError.Message); }
                        reply = new(false, "Display error", ex.Message, "Unavailable", [], engine.Pending);
                    }
                    await Send(writer, reply);
                    if (command.Stop) break;
                }
            }
            catch (Exception ex) { AppPaths.Log("Guardian disconnected or stopped: " + ex.Message); }
            finally
            {
                SystemEvents.SessionSwitch -= SessionChanged;
                SystemEvents.PowerModeChanged -= PowerChanged;
                if (engine is not null) await RecoverAsync(engine);
            }
            return engine is null || engine.Pending ? 2 : 0;
        }
    }

    private static Task Send(StreamWriter writer, Reply reply) => writer.WriteLineAsync(JsonSerializer.Serialize(reply));

    private static async Task RecoverAsync(DisplayEngine engine)
    {
        // Stay available briefly for topology changes, then leave the journal intact
        // for next launch. No guessed monitor identity and no reset-to-identity ramp.
        for (int i = 0; i < 12; i++)
        {
            try { engine.Restore(); }
            catch (Exception ex) { AppPaths.Log("Restore retry: " + ex.Message); }
            if (!engine.Pending) return;
            await Task.Delay(500);
        }
        AppPaths.Log("Recovery remains pending. Original values retained in recovery.json.");
    }

    private static void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Volatile.Write(ref _sessionBlocked, 1);
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon
                 or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Volatile.Write(ref _sessionBlocked, 0);
    }
    private static void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Volatile.Write(ref _suspended, 1);
        if (e.Mode == PowerModes.Resume) Volatile.Write(ref _suspended, 0);
    }
}
