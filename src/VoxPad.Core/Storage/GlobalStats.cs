using System.Text.Json;

namespace VoxPad.Core.Storage;

/// <summary>All-time statistics that survive list changes (stats.json). Per-sound stats live in the list itself.</summary>
public sealed class GlobalStats
{
    public long AllTimePlays { get; set; }
    public DateTime FirstRun { get; set; } = DateTime.Now;
    public DateTime? LastPlay { get; set; }
    public long Launches { get; set; }

    public static GlobalStats Load(string? path = null)
    {
        path ??= AppPaths.StatsFile;
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<GlobalStats>(File.ReadAllText(path)) ?? new(); }
        catch { }
        return new GlobalStats();
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.StatsFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logging.Log.Error("stats save", ex); }
    }
}
