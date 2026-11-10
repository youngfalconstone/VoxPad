using System.Text.Json;
using VoxPad.Audio.Decoding;
using VoxPad.Core.Loudness;
using VoxPad.Core.Logging;
using VoxPad.Core.Storage;

namespace VoxPad.Audio.Engine;

/// <summary>
/// EBU R128 normalisation. Full-file mode measures the whole file once (cached by path+size+mtime);
/// dynamic mode measures the first 6 s before playback starts and keeps refining from the live stream.
/// Gains are per channel: left and right are brought to the target independently, as Soundpad does.
/// </summary>
public sealed class Normalizer
{
    public const int DynamicAnalysisSeconds = 6;
    private readonly string _cachePath = Path.Combine(AppPaths.CacheDirectory, "loudness.json");
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly object _gate = new();

    private sealed class CacheEntry { public long Size { get; set; } public long Mtime { get; set; } public double L { get; set; } public double R { get; set; } }

    public Normalizer()
    {
        try
        {
            if (File.Exists(_cachePath))
                foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_cachePath)) ?? new())
                    _cache[kv.Key] = kv.Value;
        }
        catch (Exception ex) { Log.Warn("loudness cache unreadable: " + ex.Message); }
    }

    public double TargetLufs { get; set; } = EbuR128Meter.DbToLufs(74);

    /// <summary>Measures loudness of the first <paramref name="seconds"/> seconds (0 = whole file). Returns per-channel LUFS (NaN if silent / too short).</summary>
    public (double left, double right) Measure(string path, int seconds)
    {
        var meter = new EbuR128Meter(EngineFormat.SampleRate, EngineFormat.Channels);
        using var src = AudioFileDecoder.Open(path);
        var buf = new float[EngineFormat.SampleRate / 10 * EngineFormat.Channels];
        long limit = seconds > 0 ? (long)seconds * EngineFormat.SampleRate * EngineFormat.Channels : long.MaxValue;
        long total = 0; int n;
        while (total < limit && (n = src.Read(buf, 0, buf.Length)) > 0)
        {
            meter.Process(new ReadOnlySpan<float>(buf, 0, n));
            total += n;
        }
        return (meter.IntegratedLufs(0), meter.IntegratedLufs(1));
    }

    public (double left, double right) MeasureFullFileCached(string path)
    {
        var fi = new FileInfo(path);
        var key = path.ToLowerInvariant();
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var e) && e.Size == fi.Length && e.Mtime == fi.LastWriteTimeUtc.Ticks)
                return (e.L, e.R);
        }
        var (l, r) = Measure(path, 0);
        lock (_gate)
        {
            _cache[key] = new CacheEntry { Size = fi.Length, Mtime = fi.LastWriteTimeUtc.Ticks, L = l, R = r };
            SaveCache();
        }
        return (l, r);
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(_cache));
        }
        catch (Exception ex) { Log.Warn("loudness cache save: " + ex.Message); }
    }

    public (float left, float right) GainsFor((double left, double right) lufs) => GainsFor(lufs, TargetLufs);

    public static (float left, float right) GainsFor((double left, double right) lufs, double targetLufs)
    {
        // A channel with no measurable loudness (NaN, e.g. silent right channel) borrows the other's gain.
        double l = lufs.left, r = lufs.right;
        if (double.IsNaN(l) && !double.IsNaN(r)) l = r;
        if (double.IsNaN(r) && !double.IsNaN(l)) r = l;
        return (EbuR128Meter.GainFor(l, targetLufs), EbuR128Meter.GainFor(r, targetLufs));
    }

    /// <summary>
    /// Dynamic mode refinement: blends the live measurement into the controller (called ~once per second during playback).
    /// <paramref name="targetLufs"/> is the target the sound started with, so editing the target in Preferences
    /// mid-sound cannot suddenly boost the sound that is already playing (it applies from the next sound).
    /// </summary>
    public void Refine(GainController gain, EbuR128Meter liveMeter, double targetLufs)
    {
        var live = (liveMeter.IntegratedLufs(0), liveMeter.IntegratedLufs(1));
        if (double.IsNaN(live.Item1) && double.IsNaN(live.Item2)) return; // not enough audio yet: keep the initial estimate
        var (l, r) = GainsFor(live, targetLufs);
        // Move 50 % of the way each refinement so the level settles without pumping.
        gain.Set(gain.TargetLeft + (l - gain.TargetLeft) * 0.5f, gain.TargetRight + (r - gain.TargetRight) * 0.5f);
        Log.Debug($"Normalise refine: live {live.Item1:F1}/{live.Item2:F1} LUFS → gain {gain.TargetLeft:F3}/{gain.TargetRight:F3}");
    }
}
