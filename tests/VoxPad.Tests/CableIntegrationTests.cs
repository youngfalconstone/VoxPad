using NAudio.CoreAudioApi;
using VoxPad.Audio.Decoding;
using VoxPad.Audio.Devices;
using VoxPad.Audio.Engine;
using VoxPad.Core.Settings;
using Xunit;

namespace VoxPad.Tests;

/// <summary>
/// Real-device integration tests: they need VB-CABLE installed (CABLE Input / CABLE Output) and are skipped otherwise.
/// They drive the engine exactly like the app does and read the result back from the cable's recording endpoint,
/// i.e. what Discord/a game would hear.
/// </summary>
[Collection("audio-devices")] // real devices: never in parallel with each other
public class CableIntegrationTests
{
    private static (DeviceManager devices, AudioDeviceInfo? output) Cable()
    {
        var dm = new DeviceManager();
        var cap = dm.CaptureDevices.FirstOrDefault(d => d.IsActive && d.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase));
        return (dm, cap);
    }

    /// <summary>Records the cable output while <paramref name="during"/> runs; returns interleaved 48 kHz stereo floats.</summary>
    private static float[] RecordCable(DeviceManager dm, AudioDeviceInfo cap, double seconds, Action during)
    {
        using var dev = dm.GetDevice(cap.Id)!;
        using var pipe = new CapturePipeline(dev, loopback: false, 30);
        pipe.Start();
        Thread.Sleep(150);
        during();
        var buf = new float[(int)(seconds * EngineFormat.SampleRate) * 2];
        var chunk = new float[4800];
        int pos = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (pos < buf.Length)
        {
            int n = pipe.Read(chunk, 0, Math.Min(chunk.Length, buf.Length - pos));
            Array.Copy(chunk, 0, buf, pos, n); pos += n;
            var expected = TimeSpan.FromSeconds(pos / 2.0 / EngineFormat.SampleRate);
            var wait = expected - sw.Elapsed;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
        return buf;
    }

    private static (float peak, double rms, double hz) Analyse(float[] pcm)
    {
        int frames = pcm.Length / 2;
        // Only the region where signal is present counts (the first ~250 ms are device start-up latency).
        int first = -1, last = -1;
        for (int i = 0; i < frames; i++) if (Math.Abs(pcm[i * 2]) > 0.05f) { if (first < 0) first = i; last = i; }
        if (first < 0) return (0, 0, 0);
        float peak = 0; double sum = 0; int crossings = 0; float prev = 0;
        for (int i = first; i <= last; i++)
        {
            float v = pcm[i * 2];
            peak = Math.Max(peak, Math.Abs(v)); sum += v * v;
            if ((prev < 0 && v >= 0) || (prev >= 0 && v < 0)) crossings++;
            prev = v;
        }
        int active = last - first + 1;
        double hz = crossings / 2.0 / (active / (double)EngineFormat.SampleRate);
        return (peak, Math.Sqrt(sum / active), hz);
    }

    [Fact]
    public void RoutingResolvesToVbCable()
    {
        var (dm, cap) = Cable();
        if (cap == null) return; // no cable on this machine
        using var _ = dm;
        var status = dm.Resolve(new Preferences { SetupMode = MicSetupMode.Auto });
        Assert.NotEqual(DeviceStatusLevel.Error, status.Level);
        Assert.Equal(MicSetupMode.VbCable, status.ResolvedMode);
        Assert.Contains("CABLE Output", status.VirtualCaptureName);
    }

    [Fact]
    public void TonePlayedOnMicIsHeardOnCableOutput()
    {
        var (dm, cap) = Cable();
        using var _ = dm;
        if (cap == null) return;
        var prefs = new Preferences { SetupMode = MicSetupMode.VbCable, Normalization = NormalizationMode.Off, VoiceActivationSound = false };
        using var engine = new SoundboardEngine(dm, prefs);
        engine.ApplyDevices();
        Assert.True(engine.Mic.IsRunning, engine.Routing.Message);

        var tone = ToneGenerator.Burst(2.0, 440, 0.5f);
        var heard = RecordCable(dm, cap, 1.2, () => engine.PlayBuffer(tone, "test tone", PlayTarget.Microphone));
        engine.Stop();
        var (peak, rms, hz) = Analyse(heard);
        Assert.InRange(peak, 0.3f, 0.6f);       // 0.5 amplitude reaches the other side essentially unchanged
        Assert.InRange(rms, 0.25, 0.42);        // sine RMS = 0.354
        Assert.InRange(hz, 430, 450);           // and it is still 440 Hz
    }

    [Fact]
    public void SilenceOnCableWhenNothingPlaysAndVoiceBlocked()
    {
        var (dm, cap) = Cable();
        using var _ = dm;
        if (cap == null) return;
        var prefs = new Preferences { SetupMode = MicSetupMode.VbCable, BlockVoice = true, MicrophoneDeviceIds = { "no-such-device" } };
        using var engine = new SoundboardEngine(dm, prefs);
        engine.ApplyDevices();
        Assert.True(engine.Mic.IsRunning, engine.Routing.Message);
        var heard = RecordCable(dm, cap, 0.6, () => { });
        var (peak, _, _) = Analyse(heard);
        Assert.InRange(peak, 0f, 0.001f);
    }

    [Fact]
    public void NormalisationBringsQuietToneToTarget()
    {
        var (dm, cap) = Cable();
        using var _ = dm;
        if (cap == null) return;
        // Target 74 dB = -38 LUFS. A 0.02 amplitude sine is about -37 LUFS already; use 0.005 (~-49 LUFS) so a boost is needed.
        var prefs = new Preferences { SetupMode = MicSetupMode.VbCable, Normalization = NormalizationMode.FullFile, NormalizationTargetDb = 74, VoiceActivationSound = false, MicrophoneDeviceIds = { "no-such-device" } };
        using var engine = new SoundboardEngine(dm, prefs);
        engine.ApplyDevices();
        Assert.True(engine.Mic.IsRunning, engine.Routing.Message);
        var tone = ToneGenerator.Burst(2.0, 1000, 0.005f);
        var heard = RecordCable(dm, cap, 1.2, () => engine.PlayBuffer(tone, "quiet", PlayTarget.Microphone));
        engine.Stop();
        var m = new Core.Loudness.EbuR128Meter(EngineFormat.SampleRate, 2);
        m.Process(heard.AsSpan(heard.Length / 4)); // skip the start-up ramp
        var lufs = m.IntegratedLufs(0);
        Assert.InRange(lufs, -41, -35);
    }

    private static float PeakBetween(float[] pcm, double fromS, double toS)
    {
        int a = (int)(fromS * EngineFormat.SampleRate) * 2, b = Math.Min(pcm.Length, (int)(toS * EngineFormat.SampleRate) * 2);
        float p = 0;
        for (int i = a; i < b; i++) p = Math.Max(p, Math.Abs(pcm[i]));
        return p;
    }

    /// <summary>Regression: applying device preferences mid-sound used to cut the sound off on the microphone.</summary>
    [Fact]
    public void ApplyingDevicesMidSoundKeepsItPlaying()
    {
        var (dm, cap) = Cable();
        using var _ = dm;
        if (cap == null) return;
        var prefs = new Preferences { SetupMode = MicSetupMode.VbCable, Normalization = NormalizationMode.Off, VoiceActivationSound = false, MicrophoneDeviceIds = { "no-such-device" } };
        using var engine = new SoundboardEngine(dm, prefs);
        engine.ApplyDevices();
        Assert.True(engine.Mic.IsRunning, engine.Routing.Message);
        var tone = ToneGenerator.Burst(6.0, 440, 0.3f);
        var heard = RecordCable(dm, cap, 3.5, () =>
        {
            engine.PlayBuffer(tone, "long tone", PlayTarget.Microphone);
            new Thread(() => { Thread.Sleep(1000); engine.ApplyDevices(); }).Start();
        });
        Assert.NotNull(engine.Current);
        engine.Stop();
        Assert.InRange(PeakBetween(heard, 2.5, 3.5), 0.2f, 0.4f); // still the same tone after the restart
    }

    /// <summary>Regression: raising the normalisation target mid-sound used to blast the playing sound to full scale within a second.</summary>
    [Fact]
    public void ChangingTargetMidSoundDoesNotBoostIt()
    {
        var (dm, cap) = Cable();
        using var _ = dm;
        if (cap == null) return;
        var prefs = new Preferences { SetupMode = MicSetupMode.VbCable, Normalization = NormalizationMode.Dynamic, NormalizationTargetDb = 74, VoiceActivationSound = false, MicrophoneDeviceIds = { "no-such-device" } };
        using var engine = new SoundboardEngine(dm, prefs);
        engine.ApplyDevices();
        Assert.True(engine.Mic.IsRunning, engine.Routing.Message);
        var tone = ToneGenerator.Burst(6.0, 440, 0.05f);
        var heard = RecordCable(dm, cap, 3.5, () =>
        {
            engine.PlayBuffer(tone, "tone", PlayTarget.Microphone);
            new Thread(() => { Thread.Sleep(500); engine.Normalizer.TargetLufs = Core.Loudness.EbuR128Meter.DbToLufs(110); }).Start();
        });
        engine.Stop();
        float before = PeakBetween(heard, 0.3, 0.6), after = PeakBetween(heard, 2.0, 3.5);
        Assert.True(before > 0.005f, $"no signal before the change ({before})");
        Assert.True(after < before * 1.5f, $"level jumped from {before:F3} to {after:F3}");
    }
}
