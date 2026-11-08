using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoxPad.Audio.Decoding;
using VoxPad.Core.Logging;

namespace VoxPad.Audio.Engine;

/// <summary>
/// WASAPI capture (microphone or loopback) delivered as a 48 kHz stereo float sample provider.
/// The capture device and the render device run on different clocks, so a gentle drift compensator
/// (±0.5 % rate trim, linear interpolation) keeps the ring buffer near its target fill instead of
/// slowly accumulating latency or periodically underrunning.
/// </summary>
public sealed class CapturePipeline : ISampleProvider, IDisposable
{
    private readonly IWaveIn _capture;
    private readonly WaveFormat _srcFormat;
    private readonly float[] _ring;          // interleaved stereo frames at the source rate
    private readonly int _ringFrames;
    private readonly object _gate = new();
    private long _writeFrame, _readFrame;     // absolute frame counters
    private double _frac;                     // fractional read position for interpolation
    private readonly int _targetFillFrames;
    private float _peak;
    private float[] _conv = Array.Empty<float>();
    private volatile bool _running;

    public event Action<Exception?>? Stopped;

    public string DeviceName { get; }
    public WaveFormat WaveFormat => EngineFormat.Wave;
    public float Peak { get { var p = _peak; _peak = 0; return p; } }
    public float PeakHold => _peak;
    public bool IsRunning => _running;
    public int FillMs { get { lock (_gate) return (int)((_writeFrame - _readFrame) * 1000 / _srcFormat.SampleRate); } }

    /// <param name="device">Capture endpoint (microphone) or render endpoint (loopback) depending on <paramref name="loopback"/>.</param>
    public CapturePipeline(MMDevice device, bool loopback, int latencyMs = 30)
    {
        DeviceName = device.FriendlyName;
        _capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device, true, latencyMs);
        _srcFormat = _capture.WaveFormat;
        _ringFrames = _srcFormat.SampleRate; // 1 s
        _ring = new float[_ringFrames * 2];
        _targetFillFrames = Math.Max(1, _srcFormat.SampleRate * Math.Max(latencyMs, 20) / 1000);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) => { _running = false; Stopped?.Invoke(e.Exception); };
    }

    public void Start()
    {
        if (_running) return;
        lock (_gate) { _writeFrame = _readFrame = 0; _frac = 0; }
        _running = true;
        _capture.StartRecording();
        Log.Info($"Capture started on '{DeviceName}' ({_srcFormat.SampleRate} Hz, {_srcFormat.Channels} ch, {_srcFormat.Encoding})");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _capture.StopRecording(); } catch { }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int ch = _srcFormat.Channels;
        int bytesPerSample = _srcFormat.BitsPerSample / 8;
        int frames = e.BytesRecorded / (bytesPerSample * ch);
        if (frames <= 0) return;
        if (_conv.Length < frames * 2) _conv = new float[frames * 2];
        var buf = e.Buffer;
        bool isFloat = _srcFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (_srcFormat is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        float peak = 0;
        for (int f = 0; f < frames; f++)
        {
            float l = 0, r = 0;
            for (int c = 0; c < ch; c++)
            {
                int idx = (f * ch + c) * bytesPerSample;
                float s = bytesPerSample switch
                {
                    4 when isFloat => BitConverter.ToSingle(buf, idx),
                    4 => BitConverter.ToInt32(buf, idx) / 2147483648f,
                    3 => ((buf[idx] << 8 | buf[idx + 1] << 16 | buf[idx + 2] << 24) >> 8) / 8388608f,
                    2 => BitConverter.ToInt16(buf, idx) / 32768f,
                    _ => (buf[idx] - 128) / 128f,
                };
                if (ch == 1) { l = r = s; }
                else if (c == 0) l = s;
                else if (c == 1) r = s;
            }
            _conv[f * 2] = l; _conv[f * 2 + 1] = r;
            float a = Math.Max(Math.Abs(l), Math.Abs(r));
            if (a > peak) peak = a;
        }
        if (peak > _peak) _peak = peak;

        lock (_gate)
        {
            for (int f = 0; f < frames; f++)
            {
                int slot = (int)(_writeFrame % _ringFrames) * 2;
                _ring[slot] = _conv[f * 2];
                _ring[slot + 1] = _conv[f * 2 + 1];
                _writeFrame++;
            }
            // Overflow: the consumer is not keeping up; drop the oldest audio so latency stays bounded.
            if (_writeFrame - _readFrame > _ringFrames - 16) _readFrame = _writeFrame - _targetFillFrames;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int outFrames = count / 2;
        double baseStep = _srcFormat.SampleRate / (double)EngineFormat.SampleRate;
        lock (_gate)
        {
            long avail = _writeFrame - _readFrame;
            if (!_running || avail < 2)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            // Drift trim: speed up when we run ahead of the target fill, slow down when behind.
            double error = (avail - _targetFillFrames) / (double)_targetFillFrames;
            double step = baseStep * (1.0 + Math.Clamp(error * 0.01, -0.005, 0.005));
            for (int f = 0; f < outFrames; f++)
            {
                if (_writeFrame - _readFrame < 2)
                {
                    Array.Clear(buffer, offset + f * 2, (outFrames - f) * 2);
                    break;
                }
                int i0 = (int)(_readFrame % _ringFrames) * 2;
                int i1 = (int)((_readFrame + 1) % _ringFrames) * 2;
                float t = (float)_frac;
                buffer[offset + f * 2] = _ring[i0] + (_ring[i1] - _ring[i0]) * t;
                buffer[offset + f * 2 + 1] = _ring[i0 + 1] + (_ring[i1 + 1] - _ring[i0 + 1]) * t;
                _frac += step;
                while (_frac >= 1.0) { _frac -= 1.0; _readFrame++; }
            }
        }
        return count;
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
    }
}
