using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoxPad.Core.Logging;
using VoxPad.Core.Settings;

namespace VoxPad.Audio.Devices;

public sealed record AudioDeviceInfo(string Id, string Name, DataFlow Flow, DeviceState State, bool IsDefault, bool IsDefaultCommunications)
{
    public bool IsActive => State == DeviceState.Active;
    public override string ToString() => Name;
}

public enum DeviceStatusLevel { Good, Warning, Error }

/// <summary>Soundpad-style "Status: Good" / red with a reason, per routing setup.</summary>
public sealed record RoutingStatus(DeviceStatusLevel Level, string Message, MicSetupMode ResolvedMode, string? VirtualRenderId, string? VirtualCaptureName)
{
    public bool IsGood => Level == DeviceStatusLevel.Good;
}

/// <summary>Enumerates WASAPI endpoints, tracks hot-plug changes, detects virtual cables and Windows microphone privacy.</summary>
public sealed class DeviceManager : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Notifier _notifier;
    private readonly object _gate = new();
    private List<AudioDeviceInfo> _render = new();
    private List<AudioDeviceInfo> _capture = new();

    /// <summary>Raised (on a thread-pool thread) when any endpoint is added, removed, changes state or the default changes.</summary>
    public event Action? DevicesChanged;

    public DeviceManager()
    {
        _notifier = new Notifier(this);
        try { _enumerator.RegisterEndpointNotificationCallback(_notifier); } catch (Exception ex) { Log.Warn("Endpoint notifications unavailable: " + ex.Message); }
        Refresh();
    }

    public IReadOnlyList<AudioDeviceInfo> RenderDevices { get { lock (_gate) return _render; } }
    public IReadOnlyList<AudioDeviceInfo> CaptureDevices { get { lock (_gate) return _capture; } }

    public void Refresh()
    {
        var render = new List<AudioDeviceInfo>();
        var capture = new List<AudioDeviceInfo>();
        string? defR = SafeDefault(DataFlow.Render, Role.Multimedia), defRC = SafeDefault(DataFlow.Render, Role.Communications);
        string? defC = SafeDefault(DataFlow.Capture, Role.Multimedia), defCC = SafeDefault(DataFlow.Capture, Role.Communications);
        foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged))
        {
            try
            {
                var info = new AudioDeviceInfo(d.ID, d.FriendlyName, d.DataFlow, d.State,
                    d.ID == (d.DataFlow == DataFlow.Render ? defR : defC), d.ID == (d.DataFlow == DataFlow.Render ? defRC : defCC));
                (d.DataFlow == DataFlow.Render ? render : capture).Add(info);
            }
            catch (Exception ex) { Log.Debug("Skipping endpoint: " + ex.Message); }
            finally { d.Dispose(); }
        }
        lock (_gate) { _render = render; _capture = capture; }
    }

    private string? SafeDefault(DataFlow flow, Role role)
    {
        try { using var d = _enumerator.GetDefaultAudioEndpoint(flow, role); return d.ID; } catch { return null; }
    }

    public MMDevice? GetDevice(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try { return _enumerator.GetDevice(id); } catch { return null; }
    }

    public MMDevice? GetDefault(DataFlow flow, Role role = Role.Multimedia)
    {
        try { return _enumerator.GetDefaultAudioEndpoint(flow, role); } catch { return null; }
    }

    /// <summary>The chosen device, else the Windows default – unless that default is a virtual cable (VB-CABLE makes itself the default on install), in which case the first physical endpoint.</summary>
    public MMDevice? GetRenderOrDefault(string? id) => GetDevice(id) is { State: DeviceState.Active } d ? d : PhysicalDefault(DataFlow.Render, Role.Multimedia);
    public MMDevice? GetCaptureOrDefault(string? id) => GetDevice(id) is { State: DeviceState.Active } d ? d : PhysicalDefault(DataFlow.Capture, Role.Communications);

    public MMDevice? PhysicalDefault(DataFlow flow, Role role)
    {
        var def = GetDefault(flow, role);
        if (def != null && !LooksLikeVirtualCable(def.FriendlyName)) return def;
        var alt = (flow == DataFlow.Render ? RenderDevices : CaptureDevices).FirstOrDefault(d => d.IsActive && !LooksLikeVirtualCable(d.Name));
        if (alt != null) { Log.Info($"Default {flow} device is a virtual cable ('{def?.FriendlyName}'); using '{alt.Name}' instead"); def?.Dispose(); return GetDevice(alt.Id); }
        return def;
    }

    // ---- Microphone privacy (Settings > Privacy > Microphone) ----

    public enum MicPrivacy { Allowed, DeniedForUser, DeniedForDesktopApps, DeniedSystemWide, Unknown }

    /// <summary>Reads the CapabilityAccessManager consent store. Desktop apps are governed by the "NonPackaged" key.</summary>
    public static MicPrivacy CheckMicrophonePrivacy()
    {
        try
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
            using var hklm = Registry.LocalMachine.OpenSubKey(key);
            if (hklm?.GetValue("Value") is string sys && sys.Equals("Deny", StringComparison.OrdinalIgnoreCase)) return MicPrivacy.DeniedSystemWide;
            using var hkcu = Registry.CurrentUser.OpenSubKey(key);
            if (hkcu == null) return MicPrivacy.Unknown;
            if (hkcu.GetValue("Value") is string user && user.Equals("Deny", StringComparison.OrdinalIgnoreCase)) return MicPrivacy.DeniedForUser;
            using var np = hkcu.OpenSubKey("NonPackaged");
            if (np?.GetValue("Value") is string desktop && desktop.Equals("Deny", StringComparison.OrdinalIgnoreCase)) return MicPrivacy.DeniedForDesktopApps;
            return MicPrivacy.Allowed;
        }
        catch { return MicPrivacy.Unknown; }
    }

    // ---- Virtual cable detection ----

    private static readonly (MicSetupMode mode, string[] renderNames, string[] captureNames)[] KnownCables =
    {
        (MicSetupMode.VbCable, new[] { "CABLE Input" }, new[] { "CABLE Output" }),
        (MicSetupMode.VbCableAB, new[] { "CABLE-A Input", "CABLE-B Input" }, new[] { "CABLE-A Output", "CABLE-B Output" }),
        (MicSetupMode.Voicemeeter, new[] { "VoiceMeeter Input", "VoiceMeeter Aux Input", "VoiceMeeter VAIO3 Input" }, new[] { "VoiceMeeter Output", "VoiceMeeter Aux Output", "VoiceMeeter VAIO3 Output" }),
    };

    private static readonly string[] StereoMixNames = { "Stereo Mix", "What U Hear", "Wave Out Mix", "Stereomix", "Loopback" };

    public static bool LooksLikeVirtualCable(string name)
        => KnownCables.Any(k => k.renderNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
           || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || name.Contains("Cable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves the setup mode to a concrete render endpoint + the capture name apps must select, with a status.</summary>
    public RoutingStatus Resolve(Preferences prefs)
    {
        Refresh();
        var privacy = CheckMicrophonePrivacy();
        if (privacy is MicPrivacy.DeniedForDesktopApps or MicPrivacy.DeniedForUser or MicPrivacy.DeniedSystemWide)
            return new RoutingStatus(DeviceStatusLevel.Error, "Access denied: Windows microphone privacy blocks desktop apps. Settings > Privacy & security > Microphone > allow desktop apps.", prefs.SetupMode, null, null);

        var mics = prefs.MicrophoneDeviceIds.Count == 0
            ? CaptureDevices.Where(c => c.IsDefaultCommunications || c.IsDefault).ToList()
            : CaptureDevices.Where(c => prefs.MicrophoneDeviceIds.Contains(c.Id)).ToList();
        var micWarning = mics.Count == 0 ? "No microphone selected or available. " : mics.All(m => !m.IsActive) ? "Selected microphone is unplugged or disabled. " : "";

        var order = prefs.SetupMode == MicSetupMode.Auto
            ? new[] { MicSetupMode.VbCable, MicSetupMode.VbCableAB, MicSetupMode.Voicemeeter, MicSetupMode.GenericVirtualCable, MicSetupMode.StereoMix }
            : new[] { prefs.SetupMode };

        RoutingStatus? last = null;
        foreach (var mode in order)
        {
            var st = ResolveMode(mode, prefs, micWarning);
            if (st.IsGood || st.Level == DeviceStatusLevel.Warning) return st;
            last = st;
        }
        if (prefs.SetupMode == MicSetupMode.Auto)
            return new RoutingStatus(DeviceStatusLevel.Error, "No virtual microphone found. Install VB-CABLE (free) or Voicemeeter, then press Refresh. Speakers still work.", MicSetupMode.SpeakersOnly, null, null);
        return last ?? new RoutingStatus(DeviceStatusLevel.Error, "Unknown setup mode", prefs.SetupMode, null, null);
    }

    private RoutingStatus ResolveMode(MicSetupMode mode, Preferences prefs, string micWarning)
    {
        switch (mode)
        {
            case MicSetupMode.SpeakersOnly:
                return new RoutingStatus(DeviceStatusLevel.Warning, "Speakers only: nothing is sent to the microphone.", mode, null, null);

            case MicSetupMode.VbCable:
            case MicSetupMode.VbCableAB:
            case MicSetupMode.Voicemeeter:
            {
                var k = KnownCables.First(c => c.mode == mode);
                for (int i = 0; i < k.renderNames.Length; i++)
                {
                    var r = RenderDevices.FirstOrDefault(d => d.Name.Contains(k.renderNames[i], StringComparison.OrdinalIgnoreCase));
                    if (r == null) continue;
                    if (!r.IsActive) return new RoutingStatus(DeviceStatusLevel.Error, $"'{r.Name}' is disabled in Windows Sound settings. Enable it and press Refresh.", mode, r.Id, k.captureNames[i]);
                    var cap = CaptureDevices.FirstOrDefault(d => d.Name.Contains(k.captureNames[i], StringComparison.OrdinalIgnoreCase));
                    if (cap is { IsActive: false }) return new RoutingStatus(DeviceStatusLevel.Error, $"'{cap.Name}' (the side apps record from) is disabled. Enable it in Windows Sound settings.", mode, r.Id, cap.Name);
                    return Good(mode, r, cap?.Name ?? k.captureNames[i], micWarning);
                }
                return new RoutingStatus(DeviceStatusLevel.Error, $"{ModeName(mode)} not installed.", mode, null, null);
            }

            case MicSetupMode.GenericVirtualCable:
            {
                if (string.IsNullOrEmpty(prefs.VirtualCableRenderId))
                    return new RoutingStatus(DeviceStatusLevel.Error, "Pick the virtual cable's playback endpoint in Preferences > Devices.", mode, null, null);
                var r = RenderDevices.FirstOrDefault(d => d.Id == prefs.VirtualCableRenderId);
                if (r == null) return new RoutingStatus(DeviceStatusLevel.Error, "The chosen virtual cable endpoint is no longer present.", mode, null, null);
                if (!r.IsActive) return new RoutingStatus(DeviceStatusLevel.Error, $"'{r.Name}' is disabled or unplugged.", mode, r.Id, null);
                return Good(mode, r, "the matching recording endpoint of your cable", micWarning);
            }

            case MicSetupMode.StereoMix:
            {
                var sm = CaptureDevices.FirstOrDefault(d => StereoMixNames.Any(n => d.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));
                if (sm == null) return new RoutingStatus(DeviceStatusLevel.Error, "No 'Stereo Mix' recording device. Enable it in Sound settings > Recording (right-click > Show disabled devices).", mode, null, null);
                if (!sm.IsActive) return new RoutingStatus(DeviceStatusLevel.Error, $"'{sm.Name}' is disabled; enable it in Sound settings > Recording.", mode, null, sm.Name);
                var spk = RenderDevices.FirstOrDefault(d => d.IsDefault);
                if (spk == null) return new RoutingStatus(DeviceStatusLevel.Error, "No default playback device for Stereo Mix.", mode, null, sm.Name);
                return new RoutingStatus(DeviceStatusLevel.Warning, $"Stereo Mix: everything on '{spk.Name}' (games, music) is heard by others too. Apps must record from '{sm.Name}'.", mode, spk.Id, sm.Name);
            }
        }
        return new RoutingStatus(DeviceStatusLevel.Error, "Unknown mode", mode, null, null);
    }

    private static RoutingStatus Good(MicSetupMode mode, AudioDeviceInfo render, string captureName, string micWarning)
        => micWarning.Length > 0
            ? new RoutingStatus(DeviceStatusLevel.Warning, micWarning + $"Sounds go to '{captureName}' but your voice is not mixed in.", mode, render.Id, captureName)
            : new RoutingStatus(DeviceStatusLevel.Good, $"Good. Apps must select '{captureName}' as their microphone.", mode, render.Id, captureName);

    public static string ModeName(MicSetupMode m) => m switch
    {
        MicSetupMode.Auto => "Auto-select",
        MicSetupMode.VbCable => "VB-CABLE virtual microphone",
        MicSetupMode.VbCableAB => "VB-CABLE A/B",
        MicSetupMode.Voicemeeter => "Voicemeeter virtual input",
        MicSetupMode.GenericVirtualCable => "Other virtual cable (manual)",
        MicSetupMode.StereoMix => "Stereo Mix / What U Hear",
        MicSetupMode.SpeakersOnly => "Speakers only (no microphone)",
        _ => m.ToString(),
    };

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifier); } catch { }
        _enumerator.Dispose();
    }

    private sealed class Notifier : IMMNotificationClient
    {
        private readonly DeviceManager _owner;
        private DateTime _last;
        public Notifier(DeviceManager owner) => _owner = owner;
        private void Fire()
        {
            // Debounce: Windows fires several notifications per plug event.
            var now = DateTime.UtcNow;
            if ((now - _last).TotalMilliseconds < 300) return;
            _last = now;
            ThreadPool.QueueUserWorkItem(_ => { try { _owner.Refresh(); _owner.DevicesChanged?.Invoke(); } catch (Exception ex) { Log.Error("DevicesChanged", ex); } });
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Fire();
        public void OnDeviceAdded(string pwstrDeviceId) => Fire();
        public void OnDeviceRemoved(string deviceId) => Fire();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Fire();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
