namespace VoxPad.Audio.Engine;

/// <summary>
/// Per-channel linear gain with click-free smoothing. Written from the analysis thread, read on the audio thread.
/// Also applies a soft limiter so normalisation boosts cannot clip the virtual cable.
/// </summary>
public sealed class GainController
{
    private float _targetL = 1f, _targetR = 1f;
    private float _curL = 1f, _curR = 1f;
    private const float Smooth = 0.0005f; // per sample → ~40 ms time constant at 48 kHz

    public float TargetLeft => _targetL;
    public float TargetRight => _targetR;

    public void Set(float left, float right)
    {
        Volatile.Write(ref _targetL, Math.Clamp(left, 0f, 64f));
        Volatile.Write(ref _targetR, Math.Clamp(right, 0f, 64f));
    }

    public void SetImmediate(float left, float right)
    {
        Set(left, right);
        _curL = _targetL; _curR = _targetR;
    }

    public void Reset() => SetImmediate(1f, 1f);

    /// <summary>Applies gain in place to interleaved stereo samples.</summary>
    public void Apply(float[] buffer, int offset, int count)
    {
        float tl = Volatile.Read(ref _targetL), tr = Volatile.Read(ref _targetR);
        float cl = _curL, cr = _curR;
        for (int i = offset; i + 1 < offset + count; i += 2)
        {
            cl += (tl - cl) * Smooth;
            cr += (tr - cr) * Smooth;
            buffer[i] = SoftClip(buffer[i] * cl);
            buffer[i + 1] = SoftClip(buffer[i + 1] * cr);
        }
        _curL = cl; _curR = cr;
    }

    /// <summary>Transparent below ±0.9, smoothly compresses above so peaks never exceed 1.0.</summary>
    public static float SoftClip(float x)
    {
        const float t = 0.9f;
        float a = Math.Abs(x);
        if (a <= t) return x;
        float over = (a - t) / (1f - t);
        float y = t + (1f - t) * MathF.Tanh(over);
        return x < 0 ? -y : y;
    }
}
