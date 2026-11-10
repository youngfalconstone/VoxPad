using VoxPad.Audio.Decoding;
using VoxPad.Audio.Devices;
using VoxPad.Core.Logging;
using VoxPad.Core.Loudness;
using VoxPad.Core.Models;
using VoxPad.Core.Settings;

namespace VoxPad.Audio.Engine;

public enum PlayTarget { Both, Microphone, Speakers }
public enum PlayTrigger { UserInterface, Hotkey, Remote, AutoContinue }

/// <summary>What is currently loaded in the slots.</summary>
public sealed class PlaybackInfo
{
    public SoundEntry? Sound { get; init; }
    public string Label { get; init; } = "";
    public PlayTarget Target { get; init; }
    public PlayTrigger Trigger { get; init; }
    public TimeSpan Duration { get; init; }
    public bool OnMic { get; init; }
    public bool OnSpeakers { get; init; }
    public int Generation { get; init; }
}

/// <summary>
/// Orchestrates the microphone path and the speaker copy. Honest Soundpad constraint: one sound at a time;
/// starting a new sound replaces the current one on every output.
/// </summary>
public sealed class SoundboardEngine : IDisposable
{
    private readonly DeviceManager _devices;
    private readonly Preferences _prefs;
    private readonly object _gate = new();
    private PlaybackInfo? _current;
    private int _generation;
    private Timer? _refineTimer;
    private EbuR128Meter? _liveMeter;
    private float[]? _prerollCache;
    private string? _prerollCacheFile;

    public MicInjector Mic { get; }
    public SpeakerOutput Speakers { get; }
    public Normalizer Normalizer { get; } = new();
    public RoutingStatus Routing { get; private set; }

    /// <summary>Raised (thread-pool thread) when the current sound ends by itself. The bool is true when it ended on the mic.</summary>
    public event Action<PlaybackInfo>? Finished;
    /// <summary>Raised when something starts, stops, pauses or seeks.</summary>
    public event Action? StateChanged;
    public event Action<string>? Warning;

    public SoundboardEngine(DeviceManager devices, Preferences prefs)
    {
        _devices = devices;
        _prefs = prefs;
        Mic = new MicInjector(devices);
        Speakers = new SpeakerOutput(devices);
        Routing = new RoutingStatus(DeviceStatusLevel.Error, "Not started", MicSetupMode.Auto, null, null);
        Mic.Slot.Finished += gen => OnSlotFinished(gen, mic: true);
        Speakers.Slot.Finished += gen => OnSlotFinished(gen, mic: false);
        Mic.Failed += _ => ThreadPool.QueueUserWorkItem(_ => { Warning?.Invoke("Microphone output stopped; re-applying devices."); ApplyDevices(); });
        Speakers.Failed += _ => ThreadPool.QueueUserWorkItem(_ => { Warning?.Invoke("Speaker output stopped; re-applying devices."); ApplyDevices(); });
    }

    /// <summary>
    /// Resolves routing from preferences and (re)starts both outputs. Safe to call any time (device changes, preference edits).
    /// The current sound is kept: the playback slots outlive the device restart, so it carries on where it was instead of
    /// being cut off. An output that cannot be reopened drops its copy of the sound.
    /// </summary>
    public void ApplyDevices()
    {
        lock (_gate)
        {
            Routing = _devices.Resolve(_prefs);
            Mic.BlockVoice = _prefs.BlockVoice;
            Normalizer.TargetLufs = EbuR128Meter.DbToLufs(_prefs.NormalizationTargetDb);
            if (Routing.VirtualRenderId != null && Routing.ResolvedMode != MicSetupMode.SpeakersOnly)
            {
                if (!Mic.Start(Routing, _prefs))
                    Routing = Routing with { Level = DeviceStatusLevel.Error, Message = "Cannot open the virtual cable: " + (Mic.LastError ?? "unknown error") };
            }
            Speakers.Start(_prefs.SpeakerDeviceId, 40);
            Speakers.Volume = (float)_prefs.SpeakerVolume;
            Speakers.Muted = _prefs.SpeakerMuted;
            KeepCurrentAfterRestart();
            Log.Info($"Routing: {Routing.Level} – {Routing.Message}");
        }
        StateChanged?.Invoke();
    }

    /// <summary>After a device restart: unload slots whose output did not come back and keep <see cref="Current"/> consistent.</summary>
    private void KeepCurrentAfterRestart()
    {
        if (!Mic.IsRunning) Mic.Slot.Stop();
        if (!Speakers.IsRunning) Speakers.Slot.Stop();
        var cur = _current;
        if (cur == null) return;
        bool onMic = cur.OnMic && Mic.Slot.IsLoaded, onSpk = cur.OnSpeakers && Speakers.Slot.IsLoaded;
        if (!onMic && !onSpk) { StopRefine(); _current = null; _generation++; return; }
        if (!onMic && _refineTimer != null) StopRefine();
        if (onMic != cur.OnMic || onSpk != cur.OnSpeakers)
            _current = new PlaybackInfo { Sound = cur.Sound, Label = cur.Label, Target = cur.Target, Trigger = cur.Trigger, Duration = cur.Duration, OnMic = onMic, OnSpeakers = onSpk, Generation = cur.Generation };
    }

    public PlaybackInfo? Current { get { lock (_gate) return _current; } }
    public bool IsPlaying => Current != null && !IsPaused;
    public bool IsPaused => Mic.Slot.IsPaused || Speakers.Slot.IsPaused;
    public bool IsLoaded => Mic.Slot.IsLoaded || Speakers.Slot.IsLoaded;
    /// <summary>True while others can hear the current sound (drives the red seek bar).</summary>
    public bool AudibleToOthers => Mic.Slot.IsLoaded;
    public TimeSpan Position => Mic.Slot.IsLoaded ? Mic.Slot.Position : Speakers.Slot.Position;
    public TimeSpan Duration => Mic.Slot.IsLoaded ? Mic.Slot.Duration : Speakers.Slot.Duration;
    public float MicPeak => Mic.TakeMicPeak();

    public PlayTarget TargetFor(PlayMode mode) => mode switch
    {
        PlayMode.MicrophoneOnly => PlayTarget.Microphone,
        PlayMode.SpeakersOnly => PlayTarget.Speakers,
        _ => PlayTarget.Both,
    };

    /// <summary>Plays a file from the list. Returns null on failure (missing/undecodable file) after raising <see cref="Warning"/>.</summary>
    public PlaybackInfo? Play(SoundEntry sound, PlayTarget target, PlayTrigger trigger)
    {
        if (!File.Exists(sound.FilePath))
        {
            sound.IsMissing = true;
            Warning?.Invoke($"File not found: {sound.FilePath}");
            return null;
        }
        return PlayCore(sound, sound.Tag, () => AudioFileDecoder.Open(sound.FilePath), sound.FilePath, null, target, trigger);
    }

    /// <summary>Plays an in-memory buffer (TTS line, editor selection, recorder review).</summary>
    public PlaybackInfo? PlayBuffer(float[] pcm, string label, PlayTarget target, PlayTrigger trigger = PlayTrigger.UserInterface)
    {
        if (pcm.Length == 0) return null;
        return PlayCore(null, label, () => MemoryWaveStream.AsSource(pcm), null, pcm, target, trigger);
    }

    private PlaybackInfo? PlayCore(SoundEntry? sound, string label, Func<DecodedSource> open, string? path, float[]? buffer, PlayTarget target, PlayTrigger trigger)
    {
        lock (_gate)
        {
            StopRefine();
            bool wantMic = target != PlayTarget.Speakers;
            bool wantSpk = target != PlayTarget.Microphone;
            bool onMic = wantMic && Mic.IsRunning;
            bool onSpk = wantSpk && Speakers.IsRunning;
            if (wantMic && !onMic && !wantSpk)
            {
                Warning?.Invoke("Microphone output is not available: " + Routing.Message);
                return null;
            }
            if (!onMic && !onSpk) { Warning?.Invoke("No audio output available"); return null; }

            DecodedSource? micSrc = null, spkSrc = null;
            try
            {
                if (onMic) micSrc = open();
                if (onSpk) spkSrc = open();
            }
            catch (Exception ex)
            {
                micSrc?.Dispose(); spkSrc?.Dispose();
                Log.Error($"Cannot decode '{label}'", ex);
                Warning?.Invoke($"Cannot play '{label}': {ex.Message}");
                return null;
            }

            // Stop whatever is playing on both outputs (one sound at a time).
            Mic.Slot.Stop(); Speakers.Slot.Stop();
            int gen = ++_generation;
            var duration = (micSrc ?? spkSrc)!.TotalTime;

            // Voice-activation pre-roll on the mic (and optionally headphones).
            FloatArraySampleProvider? preMic = null, preSpk = null;
            TimeSpan preLen = TimeSpan.Zero;
            if (onMic && _prefs.VoiceActivationSound)
            {
                var pre = GetPreroll();
                preMic = new FloatArraySampleProvider(pre);
                preLen = preMic.Duration;
                if (onSpk && _prefs.VoiceActivationOnHeadphones) preSpk = new FloatArraySampleProvider(pre);
            }

            // Normalisation on the microphone path. The target is fixed for this sound (see Normalizer.Refine).
            EbuR128Meter? meter = null;
            double targetLufs = Normalizer.TargetLufs;
            if (onMic)
            {
                Mic.Slot.Gain.Reset();
                try
                {
                    switch (_prefs.Normalization)
                    {
                        case NormalizationMode.FullFile:
                        {
                            var lufs = path != null ? Normalizer.MeasureFullFileCached(path) : MeasureBuffer(buffer!, 0);
                            var (gl, gr) = Normalizer.GainsFor(lufs);
                            Mic.Slot.Gain.SetImmediate(gl, gr);
                            Log.Debug($"Normalise (full): {lufs.Item1:F1}/{lufs.Item2:F1} LUFS → gain {gl:F3}/{gr:F3}");
                            break;
                        }
                        case NormalizationMode.Dynamic:
                        {
                            var lufs = path != null ? Normalizer.Measure(path, Normalizer.DynamicAnalysisSeconds) : MeasureBuffer(buffer!, Normalizer.DynamicAnalysisSeconds);
                            var (gl, gr) = Normalizer.GainsFor(lufs);
                            Mic.Slot.Gain.SetImmediate(gl, gr);
                            Log.Debug($"Normalise (dynamic, first {Normalizer.DynamicAnalysisSeconds}s): {lufs.Item1:F1}/{lufs.Item2:F1} LUFS → gain {gl:F3}/{gr:F3}");
                            meter = _liveMeter = new EbuR128Meter(EngineFormat.SampleRate, EngineFormat.Channels);
                            break;
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("Normalisation analysis failed: " + ex.Message); }
            }

            if (micSrc != null) Mic.Slot.Load(micSrc, preMic, preLen, meter);
            if (spkSrc != null) Speakers.Slot.Load(spkSrc, preSpk, preSpk?.Duration ?? TimeSpan.Zero, null);

            _current = new PlaybackInfo { Sound = sound, Label = label, Target = target, Trigger = trigger, Duration = duration, OnMic = onMic, OnSpeakers = onSpk, Generation = gen };
            if (meter != null) _refineTimer = new Timer(_ => { try { Normalizer.Refine(Mic.Slot.Gain, meter, targetLufs); } catch { } }, null, 1000, 1000);
            Log.Info($"Play '{label}' mic={onMic} spk={onSpk} trigger={trigger} dur={duration.TotalSeconds:F1}s");
        }
        StateChanged?.Invoke();
        return Current;
    }

    private (double, double) MeasureBuffer(float[] pcm, int seconds)
    {
        var m = new EbuR128Meter(EngineFormat.SampleRate, EngineFormat.Channels);
        int limit = seconds > 0 ? Math.Min(pcm.Length, seconds * EngineFormat.SampleRate * EngineFormat.Channels) : pcm.Length;
        m.Process(new ReadOnlySpan<float>(pcm, 0, limit));
        return (m.IntegratedLufs(0), m.IntegratedLufs(1));
    }

    private float[] GetPreroll()
    {
        var file = _prefs.VoiceActivationFile;
        if (!string.IsNullOrEmpty(file) && File.Exists(file))
        {
            if (_prerollCache == null || _prerollCacheFile != file)
            {
                try { _prerollCache = AudioFileDecoder.LoadFully(file, 3); _prerollCacheFile = file; }
                catch (Exception ex) { Log.Warn("Voice activation file unreadable: " + ex.Message); _prerollCache = null; }
            }
            if (_prerollCache != null) return _prerollCache;
        }
        return _prerollCache ??= ToneGenerator.Burst(0.25, 1000, 0.7f);
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopRefine();
            Mic.Slot.Stop();
            Speakers.Slot.Stop();
            _current = null;
            _generation++;
        }
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        Mic.Slot.Pause(); Speakers.Slot.Pause();
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        Mic.Slot.Resume(); Speakers.Slot.Resume();
        StateChanged?.Invoke();
    }

    public void TogglePause() { if (IsPaused) Resume(); else if (IsLoaded) Pause(); }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            Mic.Slot.Seek(position);
            Speakers.Slot.Seek(position);
        }
        StateChanged?.Invoke();
    }

    public void Jump(int seconds) => Seek(Position + TimeSpan.FromSeconds(seconds));

    private void StopRefine()
    {
        _refineTimer?.Dispose(); _refineTimer = null;
        _liveMeter = null;
    }

    private void OnSlotFinished(int slotGeneration, bool mic)
    {
        PlaybackInfo? info;
        lock (_gate)
        {
            info = _current;
            if (info == null) return;
            // The mic slot is authoritative when present; otherwise the speaker slot.
            if (info.OnMic && !mic) return;
            if (mic && Mic.Slot.Generation != slotGeneration) return;
            if (!mic && Speakers.Slot.Generation != slotGeneration) return;
            StopRefine();
            // Let the other output finish on its own; just clear the current descriptor.
            _current = null;
        }
        ThreadPool.QueueUserWorkItem(_ => { Finished?.Invoke(info); StateChanged?.Invoke(); });
    }

    public void Dispose()
    {
        Stop();
        Mic.Dispose();
        Speakers.Dispose();
    }
}
