using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using VoxPad.Audio.Decoding;

namespace VoxPad.Audio.Tts;

/// <summary>Windows SAPI text-to-speech rendered straight into engine-format buffers.</summary>
public sealed class TtsEngine : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public IReadOnlyList<string> Voices => _synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
    public string CurrentVoice => _synth.Voice.Name;
    public int Rate { get => _synth.Rate; set => _synth.Rate = Math.Clamp(value, -10, 10); }

    public void SelectVoice(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        try { _synth.SelectVoice(name); } catch { }
    }

    /// <summary>Synthesises to 48 kHz stereo float.</summary>
    public float[] Synthesize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();
        using var ms = new MemoryStream();
        lock (_synth)
        {
            _synth.SetOutputToAudioStream(ms, new SpeechAudioFormatInfo(EngineFormat.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Stereo));
            _synth.Speak(text);
            _synth.SetOutputToNull();
        }
        var bytes = ms.ToArray();
        var pcm = new float[bytes.Length / 2];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        return pcm;
    }

    public void Dispose() => _synth.Dispose();
}
