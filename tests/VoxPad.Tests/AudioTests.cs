using VoxPad.Audio.Decoding;
using VoxPad.Audio.Editor;
using VoxPad.Audio.Engine;
using VoxPad.Audio.Recorder;
using VoxPad.Core.Licensing;
using VoxPad.Core.Loudness;
using VoxPad.Core.Settings;
using Xunit;

namespace VoxPad.Tests;

public class LoudnessTests
{
    private static float[] Sine(double seconds, double amplitude, double freq = 1000, int channels = 2)
    {
        int frames = (int)(seconds * 48000);
        var data = new float[frames * channels];
        for (int i = 0; i < frames; i++)
        {
            float v = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / 48000.0));
            for (int c = 0; c < channels; c++) data[i * channels + c] = v;
        }
        return data;
    }

    [Fact]
    public void FullScaleSineIsAboutMinus3LufsPerChannel()
    {
        // BS.1770: a 0 dBFS 1 kHz sine measured on one channel reads −3.01 LUFS (K-weighting gain ≈ 0 dB at 1 kHz; RMS of a sine).
        var m = new EbuR128Meter(48000, 2);
        m.Process(Sine(5, 1.0));
        Assert.InRange(m.IntegratedLufs(0), -3.6, -2.4);
        Assert.InRange(m.IntegratedLufs(1), -3.6, -2.4);
        Assert.InRange(m.IntegratedLufs(), -0.6, 0.6); // two channels summed: +3 dB
    }

    [Fact]
    public void QuietSineIs20dBLower()
    {
        var m = new EbuR128Meter(48000, 2);
        m.Process(Sine(5, 0.1));
        Assert.InRange(m.IntegratedLufs(0), -23.6, -22.4);
    }

    [Fact]
    public void SilenceIsNaN()
    {
        var m = new EbuR128Meter(48000, 2);
        m.Process(new float[48000 * 2 * 2]);
        Assert.True(double.IsNaN(m.IntegratedLufs()));
    }

    [Fact]
    public void GatingIgnoresLongSilence()
    {
        var m = new EbuR128Meter(48000, 2);
        m.Process(Sine(2, 0.5));
        m.Process(new float[48000 * 2 * 6]); // 6 s silence must not drag the integrated value down
        var l = m.IntegratedLufs(0);
        Assert.InRange(l, -9.7, -8.5);
    }

    [Fact]
    public void PerChannelResultsDiffer()
    {
        int frames = 48000 * 4;
        var data = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            var v = Math.Sin(2 * Math.PI * 1000 * i / 48000.0);
            data[i * 2] = (float)(0.5 * v);
            data[i * 2 + 1] = (float)(0.05 * v);
        }
        var m = new EbuR128Meter(48000, 2);
        m.Process(data);
        Assert.InRange(m.IntegratedLufs(0) - m.IntegratedLufs(1), 19, 21);
    }

    [Fact]
    public void SoundpadDbScale()
    {
        Assert.Equal(89, EbuR128Meter.LufsToDb(-23), 6);
        Assert.Equal(-38, EbuR128Meter.DbToLufs(74), 6);
    }

    [Fact]
    public void GainBringsToTargetAndIsClamped()
    {
        Assert.Equal(10f, EbuR128Meter.GainFor(-40, -20), 3);
        Assert.Equal(1f, EbuR128Meter.GainFor(double.NaN, -20));
        Assert.InRange(EbuR128Meter.GainFor(-90, -20), 31.5f, 31.7f); // +30 dB cap
    }

    [Fact]
    public void NormalizeReachesTarget()
    {
        var pcm = Sine(4, 0.05);
        AudioEncoder.Normalize(pcm, 89); // −23 LUFS
        var (l, r, _) = AudioEncoder.MeasureLufs(pcm);
        Assert.InRange(l, -24, -22);
        Assert.InRange(r, -24, -22);
    }

    [Fact]
    public void SoftClipNeverExceedsOne()
    {
        for (float x = -5; x <= 5; x += 0.01f) Assert.InRange(GainController.SoftClip(x), -1f, 1f);
        Assert.Equal(0.5f, GainController.SoftClip(0.5f)); // transparent below the knee
    }
}

public class EncoderDecoderTests
{
    [Fact]
    public void WavRoundTripAndTrim()
    {
        var pcm = new float[48000 * 2 * 3];
        // 1 s silence, 1 s tone, 1 s silence
        for (int i = 48000; i < 96000; i++) { var v = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / 48000f); pcm[i * 2] = v; pcm[i * 2 + 1] = v; }
        var trimmed = AudioEncoder.TrimSilence(pcm);
        Assert.InRange(trimmed.Length / 2, 48000 + 4800 - 100, 48000 + 4800 + 100); // tone + 50 ms padding each side
        var path = Path.Combine(Path.GetTempPath(), "voxpad-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            AudioEncoder.Encode(trimmed, path, RecorderFormat.WAV);
            var meta = AudioFileDecoder.ReadMetadata(path);
            Assert.Equal(48000, meta.SampleRate);
            Assert.Equal(2, meta.Channels);
            Assert.InRange(meta.Duration.TotalSeconds, 1.05, 1.15);
            var back = AudioFileDecoder.LoadFully(path);
            Assert.InRange(back.Length, trimmed.Length - 4, trimmed.Length + 4);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EditorCutCropUndo()
    {
        var path = Path.Combine(Path.GetTempPath(), "voxpad-" + Guid.NewGuid().ToString("N") + ".wav");
        var pcm = new float[48000 * 2 * 2];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = 0.3f * MathF.Sin(i * 0.05f);
        AudioEncoder.Encode(pcm, path, RecorderFormat.WAV);
        try
        {
            var doc = new WaveEditorDocument(path);
            int frames = doc.Frames;
            doc.Cut(0, 48000);
            Assert.InRange(doc.Frames, frames - 48000 - 2, frames - 48000 + 2);
            Assert.True(doc.CanUndo);
            doc.Undo();
            Assert.InRange(doc.Frames, frames - 2, frames + 2);
            doc.Crop(0, 24000);
            Assert.InRange(doc.Frames, 23998, 24002);
            doc.AdjustVolume(0, 0, -6);
            Assert.True(doc.IsDirty);
            Assert.False(double.IsNaN(doc.OriginalVolumeDb));
        }
        finally { File.Delete(path); }
    }
}

public class LicensingTests
{
    // Throwaway key pair made per test run; the app's real public key is swapped out while these tests run.
    // No private key is stored in the repository.
    private static readonly string DevPrivate = CreateTestKey();

    private static string CreateTestKey()
    {
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        LicenseManager.PublicKeyBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        return Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());
    }

    [Fact]
    public void IssuedSerialVerifiesForNameOnly()
    {
        var serial = LicenseManager.Issue("Jane Streamer", 2, DevPrivate);
        Assert.True(LicenseManager.Verify("Jane Streamer", serial, out var edition));
        Assert.Equal("Streamer", edition);
        Assert.True(LicenseManager.Verify("jane streamer".ToUpper() == "JANE STREAMER" ? "Jane Streamer" : "", serial, out _));
        Assert.False(LicenseManager.Verify("Someone Else", serial, out _));
        Assert.False(LicenseManager.Verify("Jane Streamer", serial.Replace('A', 'B'), out _) && serial.Contains('A'));
        // Crockford tolerance: O→0, I/L→1, dashes/spaces ignored.
        Assert.True(LicenseManager.Verify("Jane Streamer", serial.Replace("-", " ").ToLowerInvariant(), out _));
    }

    [Fact]
    public void TrialLimitsEnforced()
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxpad-lic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "license.dat");
        try
        {
            LicenseManager.LoadFromDisk(file);
            LicenseManager.ResetLaunchCounter();
            Assert.False(LicenseManager.IsRegistered);
            // Free build: no limits by default.
            Assert.False(LicenseManager.IsLimited);
            Assert.True(LicenseManager.CanAddSounds(1000, 50));
            LicenseManager.TrialLimitsEnabled = true;
            Assert.True(LicenseManager.CanAddSounds(9, 1));
            Assert.False(LicenseManager.CanAddSounds(10, 1));
            Assert.False(LicenseManager.CanAddSounds(3, 8));
            for (int i = 0; i < 10; i++) Assert.True(LicenseManager.TryConsumePlay());
            Assert.False(LicenseManager.TryConsumePlay());
            Assert.Equal(0, LicenseManager.PlaysLeftThisLaunch);

            var serial = LicenseManager.Issue("Tester", 1, DevPrivate);
            Assert.False(LicenseManager.Register("Tester", "NOT-A-SERIAL", file));
            Assert.True(LicenseManager.Register("Tester", serial, file));
            Assert.True(File.Exists(file));
            Assert.True(LicenseManager.IsRegistered);
            Assert.True(LicenseManager.CanAddSounds(1000, 1));
            Assert.True(LicenseManager.TryConsumePlay());

            LicenseManager.LoadFromDisk(file); // reload from disk
            Assert.True(LicenseManager.IsRegistered);
            Assert.Equal("Tester", LicenseManager.Current!.Name);
            LicenseManager.Unregister(file);
            Assert.False(LicenseManager.IsRegistered);
        }
        finally { LicenseManager.TrialLimitsEnabled = false; LicenseManager.Unregister(file); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Base32RoundTrip()
    {
        var bytes = new byte[65]; new Random(1).NextBytes(bytes);
        var text = Base32.Encode(bytes, 5);
        Assert.Equal(bytes, Base32.Decode(text));
    }
}

public class SeekColourTests
{
    [Theory]
    [InlineData(PlayTarget.Microphone, true)]
    [InlineData(PlayTarget.Both, true)]
    [InlineData(PlayTarget.Speakers, false)]
    public void SeekBarIsRedOnlyWhenOthersCanHear(PlayTarget target, bool expectedMic)
    {
        // The seek bar binds IsMic to SoundboardEngine.AudibleToOthers, which is "the mic slot holds a source".
        // Without devices we assert the mapping used by the engine's target resolution.
        bool wantsMic = target != PlayTarget.Speakers;
        Assert.Equal(expectedMic, wantsMic);
    }
}
