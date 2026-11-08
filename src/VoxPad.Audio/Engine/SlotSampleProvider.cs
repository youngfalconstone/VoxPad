using NAudio.Wave;
using VoxPad.Audio.Decoding;
using VoxPad.Core.Loudness;

namespace VoxPad.Audio.Engine;

/// <summary>
/// The single playback slot of one output (microphone or speakers). Soundpad plays one sound at a time;
/// loading a new source replaces the current one. Emits silence when idle so the mixer keeps running.
/// Position is tracked by counting output frames (sample-accurate, resampler-independent).
/// </summary>
public sealed class SlotSampleProvider : ISampleProvider
{
    private readonly object _gate = new();
    private DecodedSource? _source;
    private ISampleProvider? _preroll;
    private long _prerollFrames;
    private long _framesOut;      // frames of the real sound delivered since the last seek/load
    private long _seekBaseFrames; // frames corresponding to the seek position
    private bool _paused;
    private int _generation;
    private EbuR128Meter? _meter;
    private bool _everPlayed;

    public WaveFormat WaveFormat => EngineFormat.Wave;
    public GainController Gain { get; } = new();

    /// <summary>Fired on the audio thread when the current sound reaches its end (not on Stop).</summary>
    public event Action<int>? Finished;

    public bool IsLoaded { get { lock (_gate) return _source != null; } }
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public bool IsActive { get { lock (_gate) return _source != null && !_paused; } }
    public int Generation => _generation;

    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                if (_source == null) return TimeSpan.Zero;
                return TimeSpan.FromSeconds((_seekBaseFrames + _framesOut) / (double)EngineFormat.SampleRate);
            }
        }
    }

    public TimeSpan Duration { get { lock (_gate) return _source?.TotalTime ?? TimeSpan.Zero; } }

    /// <summary>Loads a new source (replacing the current one) with optional pre-roll and an optional loudness meter for dynamic refinement.</summary>
    public int Load(DecodedSource source, ISampleProvider? preroll, TimeSpan prerollLength, EbuR128Meter? meter)
    {
        DecodedSource? old;
        int gen;
        lock (_gate)
        {
            old = _source;
            _source = source;
            _preroll = preroll;
            _prerollFrames = (long)(prerollLength.TotalSeconds * EngineFormat.SampleRate);
            _framesOut = 0; _seekBaseFrames = 0;
            _paused = false;
            _meter = meter;
            _everPlayed = true;
            gen = ++_generation;
        }
        old?.Dispose();
        return gen;
    }

    public void Stop()
    {
        DecodedSource? old;
        lock (_gate)
        {
            old = _source;
            _source = null; _preroll = null; _meter = null;
            _paused = false;
            _generation++;
        }
        old?.Dispose();
    }

    public void Pause() { lock (_gate) if (_source != null) _paused = true; }
    public void Resume() { lock (_gate) _paused = false; }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_source == null) return;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > _source.TotalTime) position = _source.TotalTime;
            _source.CurrentTime = position;
            _seekBaseFrames = (long)(position.TotalSeconds * EngineFormat.SampleRate);
            _framesOut = 0;
            _prerollFrames = 0; _preroll = null;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        DecodedSource? src; ISampleProvider? pre; bool paused; EbuR128Meter? meter; int gen;
        lock (_gate) { src = _source; pre = _preroll; paused = _paused; meter = _meter; gen = _generation; }

        if (src == null || paused)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        int written = 0;
        // Pre-roll (voice activation burst) first.
        if (pre != null && _prerollFrames > 0)
        {
            int want = (int)Math.Min(count, _prerollFrames * EngineFormat.Channels);
            int got = pre.Read(buffer, offset, want);
            if (got <= 0) { lock (_gate) { _preroll = null; _prerollFrames = 0; } }
            else
            {
                written = got;
                lock (_gate) _prerollFrames -= got / EngineFormat.Channels;
                if (written < count && _prerollFrames > 0) { Array.Clear(buffer, offset + written, count - written); return count; }
            }
        }

        int remaining = count - written;
        int read;
        try { read = src.Read(buffer, offset + written, remaining); }
        catch { read = 0; }
        if (read > 0)
        {
            meter?.Process(new ReadOnlySpan<float>(buffer, offset + written, read));
            Gain.Apply(buffer, offset + written, read);
            lock (_gate) _framesOut += read / EngineFormat.Channels;
        }
        written += read;
        if (read < remaining)
        {
            Array.Clear(buffer, offset + written, count - written);
            bool ended;
            lock (_gate)
            {
                ended = ReferenceEquals(_source, src);
                if (ended) { _source = null; _meter = null; _preroll = null; }
            }
            if (ended)
            {
                src.Dispose();
                Finished?.Invoke(gen);
            }
        }
        return count;
    }
}
