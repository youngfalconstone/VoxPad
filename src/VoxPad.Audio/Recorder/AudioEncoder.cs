using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoxPad.Audio.Decoding;
using VoxPad.Core.Loudness;
using VoxPad.Core.Settings;

namespace VoxPad.Audio.Recorder;

/// <summary>Writes 48 kHz stereo float audio as M4A (AAC), WAV (16-bit) or MP3 via Media Foundation.</summary>
public static class AudioEncoder
{
    public static string Extension(RecorderFormat f) => f switch { RecorderFormat.WAV => ".wav", RecorderFormat.MP3 => ".mp3", _ => ".m4a" };

    public static void Encode(float[] pcm, string path, RecorderFormat format, int bitrate = 192000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = new FloatArraySampleProvider(pcm);
        switch (format)
        {
            case RecorderFormat.WAV:
                WaveFileWriter.CreateWaveFile16(path, source);
                break;
            case RecorderFormat.MP3:
                MediaFoundationApi.Startup();
                MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(source), path, bitrate);
                break;
            default:
                MediaFoundationApi.Startup();
                MediaFoundationEncoder.EncodeToAac(new SampleToWaveProvider16(source), path, bitrate);
                break;
        }
    }

    /// <summary>Trims leading and trailing silence (below −50 dBFS), keeping 50 ms of padding.</summary>
    public static float[] TrimSilence(float[] pcm, float thresholdDb = -50f)
    {
        float th = MathF.Pow(10, thresholdDb / 20);
        int ch = EngineFormat.Channels;
        int frames = pcm.Length / ch;
        int start = 0, end = frames;
        while (start < frames && Math.Max(Math.Abs(pcm[start * ch]), Math.Abs(pcm[start * ch + 1])) < th) start++;
        while (end > start && Math.Max(Math.Abs(pcm[(end - 1) * ch]), Math.Abs(pcm[(end - 1) * ch + 1])) < th) end--;
        int pad = EngineFormat.SampleRate / 20;
        start = Math.Max(0, start - pad);
        end = Math.Min(frames, end + pad);
        if (end <= start) return Array.Empty<float>();
        var result = new float[(end - start) * ch];
        Array.Copy(pcm, start * ch, result, 0, result.Length);
        return result;
    }

    /// <summary>Measures integrated loudness (whole buffer) per channel.</summary>
    public static (double left, double right, double total) MeasureLufs(float[] pcm)
    {
        var m = new EbuR128Meter(EngineFormat.SampleRate, EngineFormat.Channels);
        m.Process(pcm);
        return (m.IntegratedLufs(0), m.IntegratedLufs(1), m.IntegratedLufs());
    }

    /// <summary>Normalises the buffer in place to a target in Soundpad dB (89 dB = −23 LUFS), per channel, with soft clipping.</summary>
    public static void Normalize(float[] pcm, double targetDb)
    {
        var (l, r, _) = MeasureLufs(pcm);
        if (double.IsNaN(l) && double.IsNaN(r)) return;
        if (double.IsNaN(l)) l = r;
        if (double.IsNaN(r)) r = l;
        double target = EbuR128Meter.DbToLufs(targetDb);
        float gl = EbuR128Meter.GainFor(l, target, 40, 40), gr = EbuR128Meter.GainFor(r, target, 40, 40);
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            pcm[i] = Engine.GainController.SoftClip(pcm[i] * gl);
            pcm[i + 1] = Engine.GainController.SoftClip(pcm[i + 1] * gr);
        }
    }

    public static void ApplyGain(float[] pcm, int startFrame, int endFrame, float gain)
    {
        int ch = EngineFormat.Channels;
        for (int i = Math.Max(0, startFrame) * ch; i < Math.Min(endFrame, pcm.Length / ch) * ch; i++)
            pcm[i] = Engine.GainController.SoftClip(pcm[i] * gain);
    }
}
