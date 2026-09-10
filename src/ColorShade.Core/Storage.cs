using System.Text.Json;

namespace ColorShade.Core;

public static class JsonDisk
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Empty JSON document: " + Path.GetFileName(path));

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Move(temp, path, overwrite: true);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public interface IJournalStore
{
    RecoveryJournal Load();
    void Save(RecoveryJournal journal);
}

public sealed class JournalStore(string path) : IJournalStore
{
    public RecoveryJournal Load()
    {
        var journal = File.Exists(path) ? JsonDisk.Read<RecoveryJournal>(path) : new();
        journal.Validate();
        return journal;
    }
    public void Save(RecoveryJournal journal) { journal.Validate(); JsonDisk.Write(path, journal); }
}

public sealed class PresetStore(string directory)
{
    private string PathName => Path.Combine(directory, "presets.json");
    private sealed class PresetFile
    {
        public int SchemaVersion { get; set; } = 1;
        public List<Preset> Presets { get; set; } = [];
    }
    public List<Preset> Load()
    {
        var builtins = Preset.Defaults();
        if (!File.Exists(PathName)) { Save(builtins); return builtins; }
        var data = JsonDisk.Read<PresetFile>(PathName);
        if (data.SchemaVersion != 1 || data.Presets is null || data.Presets.Count > 200)
            throw new InvalidDataException("Unsupported presets.json format.");
        var ids = new HashSet<string>(builtins.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(builtins.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.Presets)
        {
            if (p is null) throw new InvalidDataException("Null preset entry.");
            p.Validate();
            if (p.BuiltIn) continue; // Built-in definitions remain immutable.
            if (!ids.Add(p.Id) || !names.Add(p.Name)) throw new InvalidDataException("Duplicate preset.");
            builtins.Add(p);
        }
        return builtins;
    }
    public void Save(List<Preset> presets)
    {
        if (presets.Count > 200) throw new InvalidDataException("A maximum of 200 presets is supported.");
        foreach (var p in presets) p.Validate();
        JsonDisk.Write(PathName, new PresetFile { Presets = presets });
    }
}
