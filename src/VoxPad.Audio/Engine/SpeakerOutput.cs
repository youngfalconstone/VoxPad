using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoxPad.Audio.Decoding;
using VoxPad.Audio.Devices;
using VoxPad.Core.Logging;

namespace VoxPad.Audio.Engine;

/// <summary>
/// The speaker copy: a second slot rendered on the chosen playback device through the normal Windows mixer.
/// Volume is the app's audio session volume on that device, i.e. the same slider the Windows Volume Mixer shows.
/// </summary>
public sealed class SpeakerOutput : IDisposable
{
    private readonly DeviceManager _devices;
    private WasapiOut? _out;
    private MMDevice? _device;
    private SimpleAudioVolume? _session;
    private float _pendingVolume = 1f;
    private bool _pendingMute;

    public SlotSampleProvider Slot { get; } = new();
    public bool IsRunning => _out != null;
    public string? DeviceName => _device?.FriendlyName;
    public string? LastError { get; private set; }
    public event Action<Exception?>? Failed;

    public SpeakerOutput(DeviceManager devices) => _devices = devices;

    public bool Start(string? deviceId, int latencyMs = 40)
    {
        StopOutput();
        LastError = null;
        var device = _devices.GetRenderOrDefault(deviceId);
        if (device == null) { LastError = "No playback device"; return false; }
        try
        {
            _device = device;
            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs);
            _out.PlaybackStopped += (_, e) => { if (e.Exception != null) { Log.Error("Speaker output stopped", e.Exception); Failed?.Invoke(e.Exception); } };
            _out.Init(new SampleToWaveProvider(Slot));
            _out.Play();
            BindSession();
            Log.Info($"Speaker output running on '{device.FriendlyName}'");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error("Speaker output start failed", ex);
            StopOutput();
            return false;
        }
    }

    /// <summary>Finds this process's audio session on the device so the slider is bound to the Windows mixer.</summary>
    private void BindSession()
    {
        _session = null;
        if (_device == null) return;
        try
        {
            var pid = (uint)Environment.ProcessId;
            var sessions = _device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.GetProcessID == pid) { _session = s.SimpleAudioVolume; break; }
            }
            if (_session != null)
            {
                _session.Volume = _pendingVolume;
                _session.Mute = _pendingMute;
            }
        }
        catch (Exception ex) { Log.Warn("Session volume binding failed: " + ex.Message); }
    }

    public float Volume
    {
        get { try { return _session?.Volume ?? _pendingVolume; } catch { return _pendingVolume; } }
        set
        {
            _pendingVolume = Math.Clamp(value, 0f, 1f);
            if (_session == null) BindSession();
            try { if (_session != null) _session.Volume = _pendingVolume; } catch (Exception ex) { Log.Warn("Set volume: " + ex.Message); }
        }
    }

    public bool Muted
    {
        get { try { return _session?.Mute ?? _pendingMute; } catch { return _pendingMute; } }
        set
        {
            _pendingMute = value;
            if (_session == null) BindSession();
            try { if (_session != null) _session.Mute = value; } catch (Exception ex) { Log.Warn("Set mute: " + ex.Message); }
        }
    }

    /// <summary>Stops the device and unloads the current sound.</summary>
    public void Stop()
    {
        Slot.Stop();
        StopOutput();
    }

    /// <summary>Stops only the device side; the slot keeps its sound so a restart (preference/device change) continues it.</summary>
    private void StopOutput()
    {
        try { _out?.Stop(); } catch { }
        _out?.Dispose(); _out = null;
        _session = null;
        _device = null;
    }

    public void Dispose() => Stop();
}
