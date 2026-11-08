namespace VoxPad.Core.Loudness;

/// <summary>
/// ITU-R BS.1770-4 / EBU R128 integrated loudness meter with per-channel results.
/// Each channel is measured as if it were a mono programme (G = 1), which is what "normalise per channel" needs;
/// the combined programme loudness is also available. Streaming: feed interleaved float samples in any chunk size.
/// </summary>
public sealed class EbuR128Meter
{
    /// <summary>Soundpad shows loudness on a positive "dB" scale where 89 dB equals −23 LUFS, i.e. dB = LUFS + 112.</summary>
    public const double SoundpadDbOffset = 112.0;
    public static double LufsToDb(double lufs) => lufs + SoundpadDbOffset;
    public static double DbToLufs(double db) => db - SoundpadDbOffset;

    private const double AbsoluteGate = -70.0;
    private const double RelativeGateLu = -10.0;
    private const int SubBlocksPerBlock = 4; // 400 ms block, 100 ms hop

    private readonly int _channels;
    private readonly int _subBlockSamples;
    private readonly KWeighting[] _filters;
    private readonly double[] _subAccum;     // running sum of squares for the sub-block in progress, per channel
    private int _subCount;
    private readonly List<double[]> _subBlocks = new(); // mean square per channel per 100 ms sub-block
    private float _peak;

    public int SampleRate { get; }
    public int Channels => _channels;
    public float PeakSample => _peak;
    public TimeSpan Duration => TimeSpan.FromSeconds(_subBlocks.Count * 0.1);

    public EbuR128Meter(int sampleRate, int channels)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        SampleRate = sampleRate;
        _channels = channels;
        _subBlockSamples = Math.Max(1, sampleRate / 10);
        _filters = new KWeighting[channels];
        for (int c = 0; c < channels; c++) _filters[c] = new KWeighting(sampleRate);
        _subAccum = new double[channels];
    }

    public void Reset()
    {
        foreach (var f in _filters) f.Reset();
        Array.Clear(_subAccum);
        _subCount = 0;
        _subBlocks.Clear();
        _peak = 0;
    }

    public void Process(ReadOnlySpan<float> interleaved)
    {
        int frames = interleaved.Length / _channels;
        int idx = 0;
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < _channels; c++)
            {
                float s = interleaved[idx++];
                float a = Math.Abs(s);
                if (a > _peak) _peak = a;
                double y = _filters[c].Process(s);
                _subAccum[c] += y * y;
            }
            if (++_subCount >= _subBlockSamples)
            {
                var ms = new double[_channels];
                for (int c = 0; c < _channels; c++) { ms[c] = _subAccum[c] / _subCount; _subAccum[c] = 0; }
                _subBlocks.Add(ms);
                _subCount = 0;
            }
        }
    }

    /// <summary>Integrated loudness of one channel in LUFS, or NaN when there is not enough audio (less than 400 ms) or everything is below the gate.</summary>
    public double IntegratedLufs(int channel) => Integrate(block => block[channel]);

    /// <summary>Integrated programme loudness across all channels (BS.1770 channel sum).</summary>
    public double IntegratedLufs() => Integrate(block => block.Sum());

    public double[] IntegratedLufsPerChannel()
    {
        var r = new double[_channels];
        for (int c = 0; c < _channels; c++) r[c] = IntegratedLufs(c);
        return r;
    }

    private double Integrate(Func<double[], double> power)
    {
        if (_subBlocks.Count < SubBlocksPerBlock) return double.NaN;
        // Block mean square = mean of 4 consecutive sub-blocks (75 % overlap).
        int n = _subBlocks.Count - SubBlocksPerBlock + 1;
        var blocks = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int k = 0; k < SubBlocksPerBlock; k++) sum += power(_subBlocks[i + k]);
            blocks[i] = sum / SubBlocksPerBlock;
        }
        // Absolute gate.
        double absSum = 0; int absCount = 0;
        foreach (var z in blocks)
        {
            if (ToLufs(z) > AbsoluteGate) { absSum += z; absCount++; }
        }
        if (absCount == 0) return double.NaN;
        double relThreshold = ToLufs(absSum / absCount) + RelativeGateLu;
        double sum2 = 0; int count2 = 0;
        foreach (var z in blocks)
        {
            double l = ToLufs(z);
            if (l > AbsoluteGate && l > relThreshold) { sum2 += z; count2++; }
        }
        return count2 == 0 ? double.NaN : ToLufs(sum2 / count2);
    }

    private static double ToLufs(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : -0.691 + 10.0 * Math.Log10(meanSquare);

    /// <summary>Linear gain that brings <paramref name="measuredLufs"/> to <paramref name="targetLufs"/>, clamped to ±30 dB. Returns 1 for NaN input.</summary>
    public static float GainFor(double measuredLufs, double targetLufs, double maxBoostDb = 30, double maxCutDb = 30)
    {
        if (double.IsNaN(measuredLufs) || double.IsInfinity(measuredLufs)) return 1f;
        double db = Math.Clamp(targetLufs - measuredLufs, -maxCutDb, maxBoostDb);
        return (float)Math.Pow(10, db / 20.0);
    }

    /// <summary>K-weighting: stage 1 high shelf (+4 dB above ~1.5 kHz), stage 2 high-pass (RLB) at ~38 Hz. Coefficients are derived for any sample rate.</summary>
    private sealed class KWeighting
    {
        private readonly Biquad _shelf, _hp;
        public KWeighting(int fs)
        {
            // Pre-filter (high shelf) design per BS.1770 Annex, re-derived for the given fs.
            double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
            double k = Math.Tan(Math.PI * f0 / fs);
            double vh = Math.Pow(10, g / 20), vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1 + k / q + k * k;
            _shelf = new Biquad(
                (vh + vb * k / q + k * k) / a0,
                2 * (k * k - vh) / a0,
                (vh - vb * k / q + k * k) / a0,
                2 * (k * k - 1) / a0,
                (1 - k / q + k * k) / a0);
            // RLB high-pass.
            double f1 = 38.13547087602444, q1 = 0.5003270373238773;
            double k1 = Math.Tan(Math.PI * f1 / fs);
            double d = 1 + k1 / q1 + k1 * k1;
            _hp = new Biquad(1.0, -2.0, 1.0, 2 * (k1 * k1 - 1) / d, (1 - k1 / q1 + k1 * k1) / d);
        }
        public double Process(double x) => _hp.Process(_shelf.Process(x));
        public void Reset() { _shelf.Reset(); _hp.Reset(); }
    }

    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;
        public Biquad(double b0, double b1, double b2, double a1, double a2) { _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2; }
        public double Process(double x)
        {
            // Transposed direct form II.
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }
        public void Reset() { _z1 = _z2 = 0; }
    }
}
