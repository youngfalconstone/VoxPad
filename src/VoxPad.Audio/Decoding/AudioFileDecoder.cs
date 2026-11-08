using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoxPad.Core.Logging;

namespace VoxPad.Audio.Decoding;

/// <summary>Engine-wide format: everything is converted to 48 kHz stereo float as early as possible.</summary>
public static class EngineFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public static readonly WaveFormat Wave = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
}

/// <summary>A decoded, seekable source in the engine format.</summary>
public sealed class DecodedSource : ISampleProvider, IDisposable
{
    private readonly WaveStream _stream;
    private readonly ISampleProvider _provider;
    private readonly WaveStream? _inner;

    internal DecodedSource(WaveStream stream, ISampleProvider provider, WaveStream? inner = null)
    {
        _stream = stream; _provider = provider; _inner = inner;
    }

    public WaveFormat WaveFormat => _provider.WaveFormat;
    public TimeSpan TotalTime => _stream.TotalTime;
    public TimeSpan CurrentTime { get => _stream.CurrentTime; set => _stream.CurrentTime = value < TimeSpan.Zero ? TimeSpan.Zero : value > TotalTime ? TotalTime : value; }
    public int Read(float[] buffer, int offset, int count) => _provider.Read(buffer, offset, count);
    public void Dispose() { _stream.Dispose(); _inner?.Dispose(); }
}

public sealed class AudioMetadata
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public TimeSpan Duration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public string Format { get; set; } = "";
    public long FileSize { get; set; }
}

/// <summary>Opens AAC, FLAC, M4A, MP3, MP4 audio, OGG, Opus, WAV and WMA. Media Foundation handles most; NVorbis and Concentus the Xiph formats.</summary>
public static class AudioFileDecoder
{
    public static readonly string[] SupportedExtensions = { ".aac", ".flac", ".m4a", ".mp3", ".mp4", ".ogg", ".opus", ".wav", ".wma", ".oga" };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string FileDialogFilter =>
        "Audio files|" + string.Join(";", SupportedExtensions.Select(e => "*" + e)) + "|All files|*.*";

    /// <summary>Opens a file for streaming playback, converted to 48 kHz stereo float.</summary>
    public static DecodedSource Open(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        WaveStream stream;
        ISampleProvider sp;
        switch (ext)
        {
            case ".ogg":
            case ".oga":
            {
                var v = new VorbisWaveReader(path);
                stream = v; sp = v;
                break;
            }
            case ".opus":
            {
                var o = new OpusWaveStream(path);
                stream = o; sp = o.ToSampleProvider();
                break;
            }
            case ".wav":
            {
                var w = new WaveFileReader(path);
                stream = w; sp = w.ToSampleProvider();
                break;
            }
            default:
            {
                // AudioFileReader = MediaFoundationReader (mp3/aac/m4a/mp4/wma/flac) wrapped as float samples.
                var a = new AudioFileReader(path);
                stream = a; sp = a;
                break;
            }
        }
        return new DecodedSource(stream, Conform(sp));
    }

    /// <summary>Converts any sample provider to the engine format.</summary>
    public static ISampleProvider Conform(ISampleProvider sp)
    {
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels > 2) sp = new MultiChannelToStereo(sp);
        if (sp.WaveFormat.SampleRate != EngineFormat.SampleRate) sp = new WdlResamplingSampleProvider(sp, EngineFormat.SampleRate);
        return sp;
    }

    /// <summary>Decodes a whole file to interleaved 48 kHz stereo floats (editor, recorder post-processing, full-file analysis).</summary>
    public static float[] LoadFully(string path, int maxSeconds = 0)
    {
        using var src = Open(path);
        var chunks = new List<float[]>();
        long total = 0;
        long limit = maxSeconds > 0 ? (long)maxSeconds * EngineFormat.SampleRate * EngineFormat.Channels : long.MaxValue;
        var buf = new float[EngineFormat.SampleRate * EngineFormat.Channels]; // 1 s
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0 && total < limit)
        {
            var copy = new float[n];
            Array.Copy(buf, copy, n);
            chunks.Add(copy);
            total += n;
        }
        var result = new float[Math.Min(total, limit)];
        long pos = 0;
        foreach (var c in chunks)
        {
            var take = (int)Math.Min(c.Length, result.Length - pos);
            if (take <= 0) break;
            Array.Copy(c, 0, result, pos, take);
            pos += take;
        }
        return result;
    }

    public static AudioMetadata ReadMetadata(string path)
    {
        var meta = new AudioMetadata { Format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant() };
        try { meta.FileSize = new FileInfo(path).Length; } catch { }
        try
        {
            using var tf = TagLib.File.Create(path);
            meta.Title = Clean(tf.Tag.Title);
            meta.Artist = Clean(tf.Tag.JoinedPerformers) ?? Clean(tf.Tag.JoinedAlbumArtists);
            meta.Duration = tf.Properties.Duration;
            meta.SampleRate = tf.Properties.AudioSampleRate;
            meta.Channels = tf.Properties.AudioChannels;
            meta.BitsPerSample = tf.Properties.BitsPerSample;
        }
        catch (Exception ex) { Log.Debug($"TagLib failed for {path}: {ex.Message}"); }
        if (meta.Duration == TimeSpan.Zero || meta.SampleRate == 0)
        {
            try
            {
                using var src = Open(path);
                meta.Duration = src.TotalTime;
                if (meta.SampleRate == 0) meta.SampleRate = EngineFormat.SampleRate;
                if (meta.Channels == 0) meta.Channels = EngineFormat.Channels;
            }
            catch (Exception ex) { Log.Warn($"Cannot decode {path}: {ex.Message}"); }
        }
        return meta;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Folds >2 channel audio to stereo by averaging left-ish and right-ish channels.</summary>
    private sealed class MultiChannelToStereo : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly int _ch;
        private float[] _tmp = Array.Empty<float>();
        public MultiChannelToStereo(ISampleProvider src) { _src = src; _ch = src.WaveFormat.Channels; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 2); }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            int need = frames * _ch;
            if (_tmp.Length < need) _tmp = new float[need];
            int got = _src.Read(_tmp, 0, need) / _ch;
            for (int f = 0; f < got; f++)
            {
                float l = 0, r = 0;
                for (int c = 0; c < _ch; c++)
                {
                    var s = _tmp[f * _ch + c];
                    if (c % 2 == 0) l += s; else r += s;
                }
                buffer[offset + f * 2] = l / ((_ch + 1) / 2);
                buffer[offset + f * 2 + 1] = r / (_ch / 2);
            }
            return got * 2;
        }
    }
}
