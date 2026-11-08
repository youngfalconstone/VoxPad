using NAudio.Wave;

namespace VoxPad.Audio.Decoding;

/// <summary>In-memory 48 kHz stereo float buffer exposed as a seekable WaveStream (TTS output, editor previews, recorder review).</summary>
public sealed class MemoryWaveStream : WaveStream
{
    private readonly float[] _samples;
    private long _position; // in bytes

    public MemoryWaveStream(float[] samples) => _samples = samples;

    public override WaveFormat WaveFormat => EngineFormat.Wave;
    public override long Length => (long)_samples.Length * 4;
    public override long Position { get => _position; set => _position = Math.Clamp(value - value % BlockAlign, 0, Length); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = (int)Math.Min(count, Length - _position);
        if (n <= 0) return 0;
        Buffer.BlockCopy(_samples, (int)_position, buffer, offset, n);
        _position += n;
        return n;
    }

    public static DecodedSource AsSource(float[] samples)
    {
        var s = new MemoryWaveStream(samples);
        return new DecodedSource(s, s.ToSampleProvider());
    }
}

/// <summary>Plays a float array once; used for the voice-activation pre-roll and notification beeps.</summary>
public sealed class FloatArraySampleProvider : ISampleProvider
{
    private readonly float[] _data;
    private int _pos;
    public FloatArraySampleProvider(float[] data) => _data = data;
    public WaveFormat WaveFormat => EngineFormat.Wave;
    public int Frames => _data.Length / EngineFormat.Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds(Frames / (double)EngineFormat.SampleRate);
    public int Read(float[] buffer, int offset, int count)
    {
        int n = Math.Min(count, _data.Length - _pos);
        if (n <= 0) return 0;
        Array.Copy(_data, _pos, buffer, offset, n);
        _pos += n;
        return n;
    }
}

public static class ToneGenerator
{
    /// <summary>A short sine burst with linear fades: the default voice-activation pre-roll and the recorder notification sound.</summary>
    public static float[] Burst(double seconds, double frequencyHz, float amplitude, double fadeSeconds = 0.01)
    {
        int frames = (int)(seconds * EngineFormat.SampleRate);
        int fade = (int)(fadeSeconds * EngineFormat.SampleRate);
        var data = new float[frames * EngineFormat.Channels];
        for (int i = 0; i < frames; i++)
        {
            float env = 1f;
            if (i < fade) env = i / (float)fade;
            else if (i > frames - fade) env = (frames - i) / (float)fade;
            float v = amplitude * env * MathF.Sin((float)(2 * Math.PI * frequencyHz * i / EngineFormat.SampleRate));
            data[i * 2] = v; data[i * 2 + 1] = v;
        }
        return data;
    }
}
