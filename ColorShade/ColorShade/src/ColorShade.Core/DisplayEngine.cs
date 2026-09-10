namespace ColorShade.Core;

public interface IDisplaySystem : IDisposable
{
    List<DisplayTarget> Enumerate();
    ushort[] ReadGamma(DisplayTarget target);
    void WriteGamma(DisplayTarget target, ushort[] gamma);
    SaturationState? ReadSaturation(DisplayTarget target);
    void WriteSaturation(DisplayTarget target, int value);
}

// Exactly one guardian owns this engine. No concurrent native calls or competing
// restore paths. A durable journal is committed BEFORE the first hardware write.
public sealed class DisplayEngine
{
    private readonly IDisplaySystem _system;
    private readonly IJournalStore _store;
    private readonly RecoveryJournal _journal;
    private string? _activeKey;
    private (int Vibrance, int Brightness)? _last;
    private DateTime _verifyAfter;
    private string? _blockedDevice;
    private string _blockedReason = "";
    public bool Pending => _journal.Displays.Count != 0;

    public DisplayEngine(IDisplaySystem system, IJournalStore store)
    {
        _system = system; _store = store; _journal = store.Load(); _journal.Validate();
    }

    public Reply Update(Command command)
    {
        var displays = _system.Enumerate();
        if (command.Refresh) { _blockedDevice = null; _last = null; }
        DisplayTarget? target = command.TargetDevice is null ? null : displays.SingleOrDefault(
            d => string.Equals(d.DeviceName, command.TargetDevice, StringComparison.OrdinalIgnoreCase));
        bool valid = target?.CanAdjust == true;
        if (!valid || !string.Equals(target!.Key, _activeKey, StringComparison.OrdinalIgnoreCase))
        {
            Restore(displays);
            if (Pending) return Result(false, "Recovery pending", "Reconnect the original SDR monitor and use Restore / Retry.", displays);
        }
        if (command.TargetDevice is null)
        {
            _blockedDevice = null;
            return Result(false, "Idle (Desktop)", "Original desktop settings restored.", displays);
        }
        if (!valid) return Result(false, "Display unavailable", target?.BlockReason ?? "The foreground monitor is no longer connected.", displays);
        if (_blockedDevice == target!.DeviceName)
            return Result(false, "Adjustment paused", _blockedReason + " Use Restore / Retry before testing again.", displays);

        try
        {
            if (_activeKey is null)
            {
                // A neutral preset is a no-op; do not touch hardware unnecessarily.
                if (command.Vibrance == 50 && command.Brightness == 50)
                    return Result(true, ActiveText(command), "Balanced preset: desktop levels are unchanged.", displays);
                var baseline = new Baseline
                {
                    MonitorKey = target.Key, Label = target.Label,
                    Gamma = _system.ReadGamma(target), Saturation = _system.ReadSaturation(target),
                    GammaPending = true
                };
                baseline.SaturationPending = baseline.Saturation is not null;
                _journal.Displays.Add(baseline);
                try { _store.Save(_journal); }
                catch { _journal.Displays.Remove(baseline); throw; } // Never write without durable recovery.
                _activeKey = target.Key;
            }
            var saved = _journal.Displays.Single(b => b.MonitorKey == _activeKey);
            var values = (Math.Clamp(command.Vibrance, 0, 100), Math.Clamp(command.Brightness, 0, 100));
            var gamma = GammaMath.Compose(saved.Gamma, values.Item2, saved.Saturation is null ? values.Item1 : 50);
            if (_last != values)
            {
                // Saturation first: some drivers reset gamma on a color-control write.
                if (saved.Saturation is not null)
                    _system.WriteSaturation(target, saved.Saturation.Map(values.Item1));
                _system.WriteGamma(target, gamma);
                Verify(target, saved, values.Item1, gamma);
                _last = values; _verifyAfter = DateTime.UtcNow.AddSeconds(2);
            }
            else if (DateTime.UtcNow >= _verifyAfter)
            {
                // Detect conflicts; do not repeatedly fight the OS or the game.
                Verify(target, saved, values.Item1, gamma);
                _verifyAfter = DateTime.UtcNow.AddSeconds(2);
            }
            string backend = saved.Saturation is null ? "GDI gamma + contrast fallback" : "AMD ADL saturation + GDI gamma";
            return new(true, ActiveText(command), target.Label + (saved.Saturation is null
                ? ": vibrance slider controls contrast on this display." : ": display saturation enabled."), backend, displays);
        }
        catch (Exception ex)
        {
            _blockedDevice = target.DeviceName; _blockedReason = ex.Message;
            Restore(displays);
            return Result(false, Pending ? "Recovery pending" : "Adjustment paused", ex.Message, displays);
        }
    }

    private void Verify(DisplayTarget target, Baseline saved, int vibrance, ushort[] gamma)
    {
        if (!GammaMath.Matches(gamma, _system.ReadGamma(target)))
            throw new IOException("Gamma readback differed. Windows, the game, or another color utility may control this display.");
        if (saved.Saturation is not null)
        {
            var current = _system.ReadSaturation(target);
            if (current is null || Math.Abs((long)current.Current - saved.Saturation.Map(vibrance)) > Math.Max(1, saved.Saturation.Step))
                throw new IOException("The AMD driver did not retain the requested saturation.");
        }
    }

    public void Restore(List<DisplayTarget>? displays = null)
    {
        _activeKey = null; _last = null;
        displays ??= _system.Enumerate();
        foreach (var saved in _journal.Displays.ToList())
        {
            // A DISPLAY number can be reassigned. Restore only to its original device path.
            var target = displays.SingleOrDefault(d => d.Key.Equals(saved.MonitorKey, StringComparison.OrdinalIgnoreCase));
            if (target?.CanAdjust != true) continue;
            if (saved.SaturationPending)
            {
                try
                {
                    _system.WriteSaturation(target, saved.Saturation!.Current);
                    var current = _system.ReadSaturation(target);
                    if (current is not null && current.Current == saved.Saturation.Current)
                        saved.SaturationPending = false;
                }
                catch { /* Keep the original value for the next recovery attempt. */ }
            }
            if (saved.GammaPending)
            {
                try
                {
                    _system.WriteGamma(target, saved.Gamma);
                    if (GammaMath.Matches(saved.Gamma, _system.ReadGamma(target))) saved.GammaPending = false;
                }
                catch { /* A disconnected monitor or driver reset can be retried later. */ }
            }
            // If saturation is still pending, restoring it later may reset gamma again.
            if (saved.SaturationPending) saved.GammaPending = true;
            if (!saved.GammaPending && !saved.SaturationPending) _journal.Displays.Remove(saved);
            _store.Save(_journal);
        }
    }

    private Reply Result(bool active, string status, string detail, List<DisplayTarget> displays) =>
        new(active, status, detail, "Display-level controls", displays, Pending);
    private static string ActiveText(Command c) => c.Preview ? "Preview (10 seconds)" : "Active (Rust Focused)";
}
