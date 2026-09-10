using System.Runtime.InteropServices;
using ColorShade.Core;
using ColorShade.Native;

// Dependency-free executable tests. These validate failure ordering and recovery
// state against a simulated driver; they do not claim Windows hardware coverage.
var tests = new (string Name, Action Run)[]
{
    ("Neutral calibrated gamma is preserved byte-for-byte", () =>
    {
        var ramp = Calibration();
        Check(ramp.SequenceEqual(GammaMath.Compose(ramp, 50, 50)));
    }),
    ("Slider extremes remain monotonic with preserved channel endpoints", () =>
    {
        var ramp = Calibration();
        foreach (int brightness in new[] { 0, 50, 100 })
        foreach (int contrast in new[] { 0, 50, 100 })
        {
            var result = GammaMath.Compose(ramp, brightness, contrast);
            for (int c = 0; c < 3; c++)
            {
                Check(result[c * 256] == ramp[c * 256]);
                Check(result[c * 256 + 255] == ramp[c * 256 + 255]);
                for (int i = 1; i < 256; i++) Check(result[c * 256 + i] >= result[c * 256 + i - 1]);
            }
        }
    }),
    ("A failed durable journal write prevents ALL display mutations", () =>
    {
        var driver = new FakeDisplay(); var journal = new MemoryJournal { FailSave = true };
        var engine = new DisplayEngine(driver, journal);
        Check(!engine.Update(new("DISPLAY1", 65, 70)).Active);
        Check(driver.Writes.Count == 0);
    }),
    ("Journal precedes gamma and saturation writes", () =>
    {
        var journal = new MemoryJournal(); var driver = new FakeDisplay();
        driver.BeforeWrite = () => Check(journal.Disk.Displays.Count == 1);
        var engine = new DisplayEngine(driver, journal);
        Check(engine.Update(new("DISPLAY1", 65, 70)).Active);
        Check(journal.Disk.Displays[0].Gamma.SequenceEqual(Calibration()));
    }),
    ("Alt-tab restores original gamma and saturation", () =>
    {
        var driver = new FakeDisplay(); var engine = new DisplayEngine(driver, new MemoryJournal());
        engine.Update(new("DISPLAY1", 65, 70)); engine.Update(new());
        Check(driver.Gamma["A"].SequenceEqual(Calibration()));
        Check(driver.Saturation["A"] == 110); Check(!engine.Pending);
    }),
    ("Monitor switch restores the old target before writing the new one", () =>
    {
        var driver = new FakeDisplay(); var engine = new DisplayEngine(driver, new MemoryJournal());
        engine.Update(new("DISPLAY1", 65, 70)); driver.Writes.Clear();
        Check(engine.Update(new("DISPLAY2", 60, 68)).Active);
        Check(driver.Writes.Take(2).All(x => x.StartsWith("A")));
        Check(driver.Writes.Skip(2).All(x => x.StartsWith("B")));
        Check(driver.Gamma["A"].SequenceEqual(Calibration()));
    }),
    ("A new guardian recovers the journal after abrupt owner loss", () =>
    {
        var driver = new FakeDisplay(); var journal = new MemoryJournal();
        new DisplayEngine(driver, journal).Update(new("DISPLAY1", 90, 80));
        var replacement = new DisplayEngine(driver, journal); replacement.Update(new());
        Check(driver.Gamma["A"].SequenceEqual(Calibration())); Check(!replacement.Pending);
    }),
    ("Disconnect retains original values and prevents a new capture", () =>
    {
        var driver = new FakeDisplay(); var journal = new MemoryJournal(); var engine = new DisplayEngine(driver, journal);
        engine.Update(new("DISPLAY1", 70, 80)); driver.Targets.RemoveAt(0); driver.Writes.Clear();
        Check(!engine.Update(new("DISPLAY2", 65, 70)).Active); Check(engine.Pending);
        Check(driver.Writes.Count == 0); Check(journal.Disk.Displays.Single().MonitorKey == "A");
        driver.Targets.Insert(0, new("A", "DISPLAY1", "Original", true)); engine.Update(new());
        Check(!engine.Pending);
    }),
    ("A reused DISPLAY number never receives another monitor's baseline", () =>
    {
        var driver = new FakeDisplay(); var engine = new DisplayEngine(driver, new MemoryJournal());
        engine.Update(new("DISPLAY1", 70, 70)); driver.Targets[0] = new("C", "DISPLAY1", "Replacement", true);
        driver.Writes.Clear(); engine.Update(new());
        Check(engine.Pending); Check(driver.Writes.Count == 0);
    }),
    ("A partial restore keeps the failed component in the journal", () =>
    {
        var driver = new FakeDisplay(); var journal = new MemoryJournal(); var engine = new DisplayEngine(driver, journal);
        engine.Update(new("DISPLAY1", 70, 70)); driver.RejectSaturation = true; engine.Update(new());
        Check(engine.Pending && journal.Disk.Displays[0].SaturationPending);
        Check(journal.Disk.Displays[0].GammaPending); // A later saturation restore may reset gamma.
        driver.RejectSaturation = false; engine.Update(new()); Check(!engine.Pending);
    }),
    ("A rejected gamma write rolls back the earlier saturation change", () =>
    {
        var driver = new FakeDisplay { RejectAdjustedGamma = true }; var engine = new DisplayEngine(driver, new MemoryJournal());
        Check(!engine.Update(new("DISPLAY1", 70, 70)).Active);
        Check(driver.Saturation["A"] == 110); Check(!engine.Pending);
    }),
    ("Silent driver rejection is detected by readback", () =>
    {
        var driver = new FakeDisplay { IgnoreAdjustedGamma = true }; var engine = new DisplayEngine(driver, new MemoryJournal());
        Check(!engine.Update(new("DISPLAY1", 70, 90)).Active); Check(driver.Saturation["A"] == 110);
    }),
    ("HDR and ambiguous targets are not mutated", () =>
    {
        var driver = new FakeDisplay(); driver.Targets[0] = driver.Targets[0] with { CanAdjust = false, BlockReason = "HDR" };
        var engine = new DisplayEngine(driver, new MemoryJournal());
        Check(!engine.Update(new("DISPLAY1", 70, 70)).Active); Check(driver.Writes.Count == 0);
    }),
    ("Missing ADL falls back to contrast and never writes saturation", () =>
    {
        var driver = new FakeDisplay { SupportsSaturation = false }; var engine = new DisplayEngine(driver, new MemoryJournal());
        var reply = engine.Update(new("DISPLAY1", 80, 50));
        Check(reply.Active && reply.Backend.Contains("fallback"));
        Check(driver.Writes.All(x => !x.Contains("sat"))); Check(!driver.Gamma["A"].SequenceEqual(Calibration()));
        engine.Update(new()); Check(driver.Gamma["A"].SequenceEqual(Calibration()));
    }),
    ("Identical heartbeats do not repeatedly write display settings", () =>
    {
        var driver = new FakeDisplay(); var engine = new DisplayEngine(driver, new MemoryJournal());
        engine.Update(new("DISPLAY1", 70, 70)); int writes = driver.Writes.Count;
        for (int i = 0; i < 10; i++) engine.Update(new("DISPLAY1", 70, 70));
        Check(driver.Writes.Count == writes);
    }),
    ("A neutral preset does not capture or mutate the display", () =>
    {
        var driver = new FakeDisplay(); var engine = new DisplayEngine(driver, new MemoryJournal());
        Check(engine.Update(new("DISPLAY1")).Active); Check(driver.Writes.Count == 0); Check(!engine.Pending);
    }),
    ("Saturation mapping honors the current baseline, limits, and step", () =>
    {
        var state = new SaturationState(110, 0, 200, 5);
        Check(state.Map(0) == 0 && state.Map(50) == 110 && state.Map(100) == 200);
        Check(state.Map(61) % 5 == 0);
    }),
    ("Corrupt recovery data fails closed", () =>
    {
        var journal = new MemoryJournal { Disk = new() { Displays = [new() { MonitorKey = "A", Gamma = [1], GammaPending = true }] } };
        bool threw = false;
        try { _ = new DisplayEngine(new FakeDisplay(), journal); } catch (InvalidDataException) { threw = true; }
        Check(threw);
    }),
    ("Preset JSON and custom names round-trip", () =>
    {
        string dir = Path.Combine(Path.GetTempPath(), "ColorShade-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PresetStore(dir); var presets = store.Load();
            presets.Add(new("custom", "Evening \"calibration\"", 73, 65)); store.Save(presets);
            Check(store.Load().Single(p => p.Id == "custom").Brightness == 65);
            Check(File.ReadAllText(Path.Combine(dir, "presets.json")).Contains("schemaVersion"));
        }
        finally { Directory.Delete(dir, true); }
    }),
    ("Win32 structures match the Windows x64 ABI", () =>
    {
        Check(Marshal.SizeOf<Win32.PathInfo>() == 72);
        Check(Marshal.SizeOf<Win32.SourceName>() == 84);
        Check(Marshal.SizeOf<Win32.TargetName>() == 420);
        Check(Marshal.SizeOf<Win32.AdvancedColor>() == 32);
        Check(Marshal.SizeOf<Win32.MonitorInfo>() == 104);
        Check(Marshal.SizeOf<Win32.ProcessEntry>() == 568);
    })
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
static ushort[] Calibration() => FakeDisplay.Calibration();

sealed class MemoryJournal : IJournalStore
{
    public RecoveryJournal Disk = new();
    public bool FailSave;
    public RecoveryJournal Load() => Clone(Disk);
    public void Save(RecoveryJournal value)
    { if (FailSave) throw new IOException("Simulated full disk"); Disk = Clone(value); }
    private static RecoveryJournal Clone(RecoveryJournal value) =>
        System.Text.Json.JsonSerializer.Deserialize<RecoveryJournal>(System.Text.Json.JsonSerializer.Serialize(value))!;
}

sealed class FakeDisplay : IDisplaySystem
{
    public List<DisplayTarget> Targets = [new("A", "DISPLAY1", "Left", true), new("B", "DISPLAY2", "Right", true)];
    public Dictionary<string, ushort[]> Gamma = new() { ["A"] = Calibration(), ["B"] = Calibration() };
    public Dictionary<string, int> Saturation = new() { ["A"] = 110, ["B"] = 110 };
    public List<string> Writes = [];
    public bool RejectSaturation, RejectAdjustedGamma, IgnoreAdjustedGamma;
    public bool SupportsSaturation = true;
    public Action? BeforeWrite;
    public List<DisplayTarget> Enumerate() => Targets.ToList();
    public ushort[] ReadGamma(DisplayTarget t) => (ushort[])Gamma[t.Key].Clone();
    public void WriteGamma(DisplayTarget t, ushort[] value)
    {
        BeforeWrite?.Invoke();
        if (!value.SequenceEqual(Calibration()))
        {
            if (RejectAdjustedGamma) throw new IOException("Driver rejected gamma");
            if (IgnoreAdjustedGamma) return;
        }
        Writes.Add(t.Key + " gamma"); Gamma[t.Key] = (ushort[])value.Clone();
    }
    public SaturationState? ReadSaturation(DisplayTarget t) => SupportsSaturation ? new(Saturation[t.Key], 0, 200, 1) : null;
    public void WriteSaturation(DisplayTarget t, int value)
    {
        BeforeWrite?.Invoke(); if (RejectSaturation) throw new IOException("Driver unavailable");
        Writes.Add(t.Key + " sat"); Saturation[t.Key] = value;
    }
    public void Dispose() { }
    public static ushort[] Calibration() => Enumerable.Range(0, 768)
        .Select(i => (ushort)Math.Round(Math.Pow((i % 256) / 255.0, 1 + (i / 256) * .08) * (65535 - (i / 256) * 800))).ToArray();
}
