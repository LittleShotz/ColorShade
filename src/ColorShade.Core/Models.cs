namespace ColorShade.Core;

public sealed record Preset(string Id, string Name, int Vibrance, int Brightness, bool BuiltIn = false)
{
    public static List<Preset> Defaults() =>
    [
        new("balanced", "Default / Balanced", 50, 50, true),
        new("day", "Rust Day Visibility", 65, 55, true),
        new("night", "Rust Night / High Gamma", 60, 72, true)
    ];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80 || string.IsNullOrWhiteSpace(Name)
            || Name.Length > 60 || Vibrance is < 0 or > 100 || Brightness is < 0 or > 100)
            throw new InvalidDataException("Invalid preset name, identifier, or slider value.");
    }
}

public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public string SelectedPresetId { get; set; } = "balanced";
}

public sealed record DisplayTarget(string Key, string DeviceName, string Label,
    bool CanAdjust, string BlockReason = "");

public sealed record SaturationState(int Current, int Minimum, int Maximum, int Step)
{
    public bool IsValid => Minimum < Maximum && Current >= Minimum && Current <= Maximum && Step > 0;
    public int Map(int percent)
    {
        if (!IsValid) throw new InvalidDataException("Invalid driver saturation range.");
        percent = Math.Clamp(percent, 0, 100);
        if (percent == 50) return Current;
        double raw = percent < 50
            ? Minimum + ((double)Current - Minimum) * percent / 50.0
            : Current + ((double)Maximum - Current) * (percent - 50) / 50.0;
        return (int)Math.Clamp(Minimum + Math.Round((raw - Minimum) / Step) * Step, Minimum, Maximum);
    }
}

public sealed class Baseline
{
    public string MonitorKey { get; set; } = "";
    public string Label { get; set; } = "";
    public ushort[] Gamma { get; set; } = [];
    public SaturationState? Saturation { get; set; }
    public bool GammaPending { get; set; }
    public bool SaturationPending { get; set; }
}

public sealed class RecoveryJournal
{
    public int SchemaVersion { get; set; } = 1;
    public List<Baseline> Displays { get; set; } = [];
    public void Validate()
    {
        if (SchemaVersion != 1 || Displays is null || Displays.Count > 32)
            throw new InvalidDataException("Unsupported recovery journal.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Displays)
            if (b is null || string.IsNullOrWhiteSpace(b.MonitorKey) || !keys.Add(b.MonitorKey)
                || b.Gamma is null || b.Gamma.Length != 768
                || (b.SaturationPending && b.Saturation?.IsValid != true))
                throw new InvalidDataException("Recovery journal is incomplete. Keep this file for recovery.");
    }
}

public sealed record Command(string? TargetDevice = null, int Vibrance = 50, int Brightness = 50,
    bool Preview = false, bool Refresh = false, bool Stop = false);

public sealed record Reply(bool Active, string Status, string Detail, string Backend,
    List<DisplayTarget> Displays, bool RecoveryPending = false);
