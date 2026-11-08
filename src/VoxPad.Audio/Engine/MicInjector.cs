using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoxPad.Audio.Decoding;
using VoxPad.Audio.Devices;
using VoxPad.Core.Logging;
using VoxPad.Core.Settings;

namespace VoxPad.Audio.Engine;

/// <summary>
/// The microphone path: physical microphone(s) → voice gate → mixer ← soundboard slot → virtual cable render endpoint.
/// Applications that select the cable's recording endpoint hear voice + sound (or sound only with Block voice).
/// </summary>
public sealed class MicInjector : IDisposable
{
    private readonly DeviceManager _devices;
    private readonly List<CapturePipeline> _mics = new();
    private readonly MixingSampleProvider _mixer = new(EngineFormat.Wave) { ReadFully = true };
    private readonly VoiceGate _voiceGate;
    private WasapiOut? _out;
    private string? _renderId;

    public SlotSampleProvider Slot { get; } = new();
    public bool IsRunning => _out != null;
    public string? RenderDeviceName { get; private set; }
    public string? LastError { get; private set; }
    public bool BlockVoice { get; set; }

    /// <summary>Raised from the audio thread when the render device stops unexpectedly (unplugged etc.).</summary>
    public event Action<Exception?>? Failed;

    public MicInjector(DeviceManager devices)
    {
        _devices = devices;
        _voiceGate = new VoiceGate(new MixingSampleProvider(EngineFormat.Wave) { ReadFully = true }, () => BlockVoice && Slot.IsActive);
        _mixer.AddMixerInput(_voiceGate);
        _mixer.AddMixerInput(Slot);
    }

    public float MicPeak => _mics.Count == 0 ? 0 : _mics.Max(m => m.PeakHold);
    public float TakeMicPeak() => _mics.Count == 0 ? 0 : _mics.Max(m => m.Peak);

    /// <summary>(Re)starts the path for the resolved routing. Returns false and sets <see cref="LastError"/> on failure.</summary>
    public bool Start(RoutingStatus routing, Preferences prefs)
    {
        Stop();
        LastError = null;
        if (routing.VirtualRenderId == null || routing.ResolvedMode == MicSetupMode.SpeakersOnly) return false;
        var render = _devices.GetDevice(routing.VirtualRenderId);
        if (render == null || render.State != DeviceState.Active) { LastError = "Virtual cable endpoint not active"; return false; }

        // Microphones with "Use" ticked; default communications device when none ticked.
        var micDevices = new List<MMDevice>();
        if (prefs.MicrophoneDeviceIds.Count == 0)
        {
            var d = _devices.PhysicalDefault(DataFlow.Capture, Role.Communications);
            if (d != null && !DeviceManager.LooksLikeVirtualCable(d.FriendlyName)) micDevices.Add(d);
        }
        else
        {
            foreach (var id in prefs.MicrophoneDeviceIds)
            {
                var d = _devices.GetDevice(id);
                if (d is { State: DeviceState.Active }) micDevices.Add(d);
            }
        }

        try
        {
            foreach (var d in micDevices)
            {
                try
                {
                    var cp = new CapturePipeline(d, loopback: false, prefs.MicLatencyMs);
                    cp.Stopped += ex => { if (ex != null) Log.Warn($"Mic capture '{cp.DeviceName}' stopped: {ex.Message}"); };
                    _mics.Add(cp);
                    _voiceGate.VoiceMixer.AddMixerInput(cp);
                }
                catch (Exception ex) { Log.Warn($"Cannot open microphone '{d.FriendlyName}': {ex.Message}"); }
            }

            _out = new WasapiOut(render, AudioClientShareMode.Shared, true, Math.Max(prefs.MicLatencyMs, 20));
            _out.PlaybackStopped += (_, e) => { if (e.Exception != null) { Log.Error("Mic render stopped", e.Exception); Failed?.Invoke(e.Exception); } };
            _out.Init(new SampleToWaveProvider(_mixer));
            foreach (var m in _mics) m.Start();
            _out.Play();
            _renderId = render.ID;
            RenderDeviceName = render.FriendlyName;
            Log.Info($"Mic injection running → '{RenderDeviceName}' with {_mics.Count} microphone(s), block voice={BlockVoice}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error("Mic injection start failed", ex);
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        Slot.Stop();
        try { _out?.Stop(); } catch { }
        _out?.Dispose(); _out = null;
        foreach (var m in _mics) { _voiceGate.VoiceMixer.RemoveMixerInput(m); m.Dispose(); }
        _mics.Clear();
        _renderId = null; RenderDeviceName = null;
    }

    public void Dispose() => Stop();

    /// <summary>Mutes the voice (with a 5 ms ramp) while a sound plays and Block voice is on.</summary>
    private sealed class VoiceGate : ISampleProvider
    {
        private readonly Func<bool> _blocked;
        private float _gain = 1f;
        public MixingSampleProvider VoiceMixer { get; }
        public VoiceGate(MixingSampleProvider voice, Func<bool> blocked) { VoiceMixer = voice; _blocked = blocked; }
        public WaveFormat WaveFormat => EngineFormat.Wave;
        public int Read(float[] buffer, int offset, int count)
        {
            int n = VoiceMixer.Read(buffer, offset, count);
            float target = _blocked() ? 0f : 1f;
            if (_gain == target && target == 1f) return n;
            const float step = 1f / (48 * 5); // 5 ms ramp
            for (int i = 0; i < n; i += 2)
            {
                if (_gain < target) _gain = Math.Min(target, _gain + step);
                else if (_gain > target) _gain = Math.Max(target, _gain - step);
                buffer[offset + i] *= _gain;
                if (i + 1 < n) buffer[offset + i + 1] *= _gain;
            }
            return n;
        }
    }
}
