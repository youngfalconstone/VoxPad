using VoxPad.Audio.Decoding;
using VoxPad.Audio.Recorder;
using VoxPad.Core.Loudness;
using VoxPad.Core.Settings;

namespace VoxPad.Audio.Editor;

/// <summary>Editable in-memory audio (48 kHz stereo float) with undo/redo, cut, crop and volume operations.</summary>
public sealed class WaveEditorDocument
{
    public const int WarnMinutes = 10;
    private const int MaxUndo = 20;
    private readonly Stack<float[]> _undo = new();
    private readonly Stack<float[]> _redo = new();

    public float[] Samples { get; private set; }
    public string SourcePath { get; }
    public AudioMetadata Attributes { get; }
    /// <summary>Original loudness in Soundpad dB (read-only attribute).</summary>
    public double OriginalVolumeDb { get; }
    public bool IsDirty { get; private set; }
    public int Frames => Samples.Length / EngineFormat.Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds(Frames / (double)EngineFormat.SampleRate);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsLong => Attributes.Duration.TotalMinutes > WarnMinutes;

    public event Action? Changed;

    public WaveEditorDocument(string path)
    {
        SourcePath = path;
        Attributes = AudioFileDecoder.ReadMetadata(path);
        Samples = AudioFileDecoder.LoadFully(path);
        var (_, _, total) = AudioEncoder.MeasureLufs(Samples);
        OriginalVolumeDb = double.IsNaN(total) ? double.NaN : EbuR128Meter.LufsToDb(total);
    }

    private void Push()
    {
        _undo.Push(Samples);
        while (_undo.Count > MaxUndo) { var arr = _undo.ToArray(); _undo.Clear(); for (int i = arr.Length - 2; i >= 0; i--) _undo.Push(arr[i]); }
        _redo.Clear();
    }

    private void Commit(float[] next)
    {
        Push();
        Samples = next;
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Deletes the selection [start, end) in frames.</summary>
    public void Cut(int startFrame, int endFrame)
    {
        Clamp(ref startFrame, ref endFrame);
        if (endFrame <= startFrame) return;
        int ch = EngineFormat.Channels;
        var next = new float[Samples.Length - (endFrame - startFrame) * ch];
        Array.Copy(Samples, 0, next, 0, startFrame * ch);
        Array.Copy(Samples, endFrame * ch, next, startFrame * ch, Samples.Length - endFrame * ch);
        Commit(next);
    }

    /// <summary>Keeps only the selection.</summary>
    public void Crop(int startFrame, int endFrame)
    {
        Clamp(ref startFrame, ref endFrame);
        if (endFrame <= startFrame) return;
        int ch = EngineFormat.Channels;
        var next = new float[(endFrame - startFrame) * ch];
        Array.Copy(Samples, startFrame * ch, next, 0, next.Length);
        Commit(next);
    }

    /// <summary>Applies a gain in dB to the selection (or everything when the selection is empty).</summary>
    public void AdjustVolume(int startFrame, int endFrame, double gainDb)
    {
        if (endFrame <= startFrame) { startFrame = 0; endFrame = Frames; }
        Clamp(ref startFrame, ref endFrame);
        var next = (float[])Samples.Clone();
        AudioEncoder.ApplyGain(next, startFrame, endFrame, (float)Math.Pow(10, gainDb / 20));
        Commit(next);
    }

    public void NormalizeTo(double targetDb)
    {
        var next = (float[])Samples.Clone();
        AudioEncoder.Normalize(next, targetDb);
        Commit(next);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(Samples);
        Samples = _undo.Pop();
        IsDirty = true;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Push(Samples);
        Samples = _redo.Pop();
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Copy of a selection, for "play selection on speakers".</summary>
    public float[] Extract(int startFrame, int endFrame)
    {
        if (endFrame <= startFrame) { startFrame = 0; endFrame = Frames; }
        Clamp(ref startFrame, ref endFrame);
        int ch = EngineFormat.Channels;
        var r = new float[(endFrame - startFrame) * ch];
        Array.Copy(Samples, startFrame * ch, r, 0, r.Length);
        return r;
    }

    /// <summary>Min/max peaks per column for waveform drawing.</summary>
    public (float[] min, float[] max) Peaks(int columns, int startFrame, int endFrame)
    {
        Clamp(ref startFrame, ref endFrame);
        var min = new float[columns]; var max = new float[columns];
        if (columns <= 0 || endFrame <= startFrame) return (min, max);
        double framesPerCol = (endFrame - startFrame) / (double)columns;
        int ch = EngineFormat.Channels;
        for (int c = 0; c < columns; c++)
        {
            int a = startFrame + (int)(c * framesPerCol), b = startFrame + (int)((c + 1) * framesPerCol);
            if (b <= a) b = a + 1;
            float lo = 0, hi = 0;
            for (int f = a; f < b && f < Frames; f++)
            {
                float l = Samples[f * ch], r = Samples[f * ch + 1];
                float v = Math.Abs(l) > Math.Abs(r) ? l : r;
                if (v < lo) lo = v; if (v > hi) hi = v;
            }
            min[c] = lo; max[c] = hi;
        }
        return (min, max);
    }

    /// <summary>Saves in the recorder format. Returns the path written (extension follows the format).</summary>
    public string Save(RecorderFormat format, string? targetPath = null)
    {
        var path = targetPath ?? Path.ChangeExtension(SourcePath, AudioEncoder.Extension(format));
        AudioEncoder.Encode(Samples, path, format);
        IsDirty = false;
        return path;
    }

    private void Clamp(ref int start, ref int end)
    {
        start = Math.Clamp(start, 0, Frames);
        end = Math.Clamp(end, 0, Frames);
    }
}
