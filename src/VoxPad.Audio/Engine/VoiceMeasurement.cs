using NAudio.CoreAudioApi;
using VoxPad.Audio.Decoding;
using VoxPad.Audio.Devices;
using VoxPad.Core.Loudness;
using VoxPad.Core.Logging;

namespace VoxPad.Audio.Engine;

/// <summary>
/// The voice-measurement tool: records the microphone for a few seconds while the user talks and reports the
/// integrated loudness on Soundpad's dB scale. The result becomes the normalisation target (expected &gt; 74 dB).
/// </summary>
public sealed class VoiceMeasurement : IDisposable
{
    private readonly DeviceManager _devices;
    private CapturePipeline? _capture;
    private EbuR128Meter? _meter;
    private Thread? _pump;
    private volatile bool _running;

    public event Action<double /*progress 0..1*/, float /*peak*/, double /*live dB*/>? Progress;
    public event Action<double /*dB*/, bool /*tooQuiet*/>? Completed;
    public event Action<string>? Error;

    public double Seconds { get; set; } = 8;
    public bool IsRunning => _running;

    public VoiceMeasurement(DeviceManager devices) => _devices = devices;

    public void Start(string? micDeviceId)
    {
        if (_running) return;
        var dev = _devices.GetCaptureOrDefault(micDeviceId);
        if (dev == null) { Error?.Invoke("No microphone available"); return; }
        try
        {
            _capture = new CapturePipeline(dev, loopback: false, 30);
            _meter = new EbuR128Meter(EngineFormat.SampleRate, EngineFormat.Channels);
            _running = true;
            _capture.Start();
            _pump = new Thread(Pump) { IsBackground = true, Name = "VoxPad.VoiceMeasure" };
            _pump.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Voice measurement start", ex);
            Error?.Invoke(ex.Message);
            Cleanup();
        }
    }

    public void Cancel()
    {
        _running = false;
    }

    private void Pump()
    {
        var buf = new float[EngineFormat.SampleRate / 20 * EngineFormat.Channels]; // 50 ms
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long frames = 0, total = (long)(Seconds * EngineFormat.SampleRate);
        bool cancelled = false;
        while (frames < total)
        {
            if (!_running) { cancelled = true; break; }
            int n = _capture!.Read(buf, 0, buf.Length);
            _meter!.Process(new ReadOnlySpan<float>(buf, 0, n));
            frames += n / EngineFormat.Channels;
            double live = _meter.IntegratedLufs();
            Progress?.Invoke(frames / (double)total, _capture.PeakHold, double.IsNaN(live) ? double.NaN : EbuR128Meter.LufsToDb(live));
            // Reading 50 ms of audio from the ring takes no time; pace to real time.
            var expected = TimeSpan.FromSeconds(frames / (double)EngineFormat.SampleRate);
            var wait = expected - sw.Elapsed;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
        double lufs = _meter!.IntegratedLufs();
        Cleanup();
        if (cancelled) return;
        if (double.IsNaN(lufs)) { Completed?.Invoke(double.NaN, true); return; }
        var db = EbuR128Meter.LufsToDb(lufs);
        Log.Info($"Voice measured: {db:F1} dB ({lufs:F1} LUFS)");
        Completed?.Invoke(db, db < 60);
    }

    private void Cleanup()
    {
        _running = false;
        _capture?.Dispose(); _capture = null;
    }

    public void Dispose() { Cancel(); Cleanup(); }
}
