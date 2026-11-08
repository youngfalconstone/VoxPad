using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoxPad.Audio.Decoding;
using VoxPad.Audio.Devices;
using VoxPad.Audio.Engine;
using VoxPad.Core.Logging;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.Audio.Recorder;

/// <summary>
/// Records what-you-hear (loopback of a playback device) or a microphone. Audio is buffered uncompressed in %TEMP%
/// (so RAM stays flat for long takes), then trimmed, normalised and encoded to the recorder format on stop.
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly DeviceManager _devices;
    private CapturePipeline? _capture;
    private WasapiOut? _silence;          // keeps a loopback device "busy" so WASAPI delivers data during silence
    private FileStream? _temp;
    private string? _tempPath;
    private Thread? _pump;
    private volatile bool _running;
    private long _frames;
    private int _maxSeconds;

    public bool IsRecording => _running;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(_frames / (double)EngineFormat.SampleRate);
    public float Peak => _capture?.Peak ?? 0;
    public string? SourceName => _capture?.DeviceName;

    public event Action? Started;
    public event Action<string>? Error;
    /// <summary>Raised when the maximum recording time is reached (the recording is stopped automatically).</summary>
    public event Action? MaxTimeReached;

    public Recorder(DeviceManager devices) => _devices = devices;

    public bool Start(RecorderSource source, string? deviceId, int maxSeconds)
    {
        if (_running) return false;
        _maxSeconds = maxSeconds;
        try
        {
            MMDevice? dev;
            if (source == RecorderSource.Speakers)
            {
                dev = _devices.GetRenderOrDefault(deviceId);
                if (dev == null) throw new InvalidOperationException("No playback device to record from");
                _capture = new CapturePipeline(dev, loopback: true, 50);
                _silence = new WasapiOut(dev, AudioClientShareMode.Shared, true, 100);
                _silence.Init(new SilenceProvider(dev.AudioClient.MixFormat));
                _silence.Play();
            }
            else
            {
                dev = _devices.GetCaptureOrDefault(deviceId);
                if (dev == null) throw new InvalidOperationException("No microphone to record from");
                _capture = new CapturePipeline(dev, loopback: false, 30);
            }
            Directory.CreateDirectory(AppPaths.TempDirectory);
            _tempPath = Path.Combine(AppPaths.TempDirectory, $"rec-{DateTime.Now:yyyyMMdd-HHmmss}.f32");
            _temp = new FileStream(_tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
            _frames = 0;
            _running = true;
            _capture.Start();
            _pump = new Thread(Pump) { IsBackground = true, Name = "VoxPad.Recorder" };
            _pump.Start();
            Started?.Invoke();
            Log.Info($"Recording started from '{_capture.DeviceName}' (max {maxSeconds}s)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Recorder start", ex);
            Error?.Invoke(ex.Message);
            Cleanup();
            return false;
        }
    }

    private void Pump()
    {
        var buf = new float[EngineFormat.SampleRate / 10 * EngineFormat.Channels]; // 100 ms
        var bytes = new byte[buf.Length * 4];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long limitFrames = _maxSeconds > 0 ? (long)_maxSeconds * EngineFormat.SampleRate : long.MaxValue;
        while (_running)
        {
            int n = _capture!.Read(buf, 0, buf.Length);
            Buffer.BlockCopy(buf, 0, bytes, 0, n * 4);
            try { _temp!.Write(bytes, 0, n * 4); } catch (Exception ex) { Error?.Invoke(ex.Message); _running = false; break; }
            _frames += n / EngineFormat.Channels;
            if (_frames >= limitFrames) { _running = false; MaxTimeReached?.Invoke(); break; }
            var expected = TimeSpan.FromSeconds(_frames / (double)EngineFormat.SampleRate);
            var wait = expected - sw.Elapsed;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
    }

    /// <summary>Stops and returns the processed audio (trimmed/normalised as requested) in engine format, or null if nothing was captured.</summary>
    public float[]? Stop(bool trimSilence, bool normalize, double targetDb)
    {
        if (_temp == null) return null;
        _running = false;
        _pump?.Join(2000);
        _capture?.Stop();
        float[] pcm;
        try
        {
            _temp.Flush();
            _temp.Position = 0;
            pcm = new float[_temp.Length / 4];
            var bytes = new byte[1 << 16];
            long pos = 0; int n;
            while ((n = _temp.Read(bytes, 0, bytes.Length)) > 0)
            {
                Buffer.BlockCopy(bytes, 0, pcm, (int)pos, n);
                pos += n;
            }
        }
        finally { Cleanup(); }
        if (pcm.Length == 0) return null;
        if (trimSilence) pcm = AudioEncoder.TrimSilence(pcm);
        if (pcm.Length == 0) return null;
        if (normalize) AudioEncoder.Normalize(pcm, targetDb);
        Log.Info($"Recording stopped: {pcm.Length / 2 / (double)EngineFormat.SampleRate:F1}s");
        return pcm;
    }

    public void Cancel()
    {
        _running = false;
        _pump?.Join(2000);
        Cleanup();
    }

    private void Cleanup()
    {
        _running = false;
        _capture?.Dispose(); _capture = null;
        try { _silence?.Stop(); } catch { }
        _silence?.Dispose(); _silence = null;
        _temp?.Dispose(); _temp = null;
        if (_tempPath != null) { try { File.Delete(_tempPath); } catch { } _tempPath = null; }
    }

    public void Dispose() => Cancel();

    private sealed class SilenceProvider : IWaveProvider
    {
        public SilenceProvider(WaveFormat f) => WaveFormat = f;
        public WaveFormat WaveFormat { get; }
        public int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); return count; }
    }
}
