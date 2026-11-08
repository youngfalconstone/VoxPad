using System.Text.Json;
using System.Text.Json.Serialization;
using VoxPad.Core.Models;
using VoxPad.Core.Serialization;
using VoxPad.Core.Storage;

namespace VoxPad.Core.Settings;

public enum UiTheme { Light, Dark, Native }
public enum UpdateCheckInterval { Never, Daily, Weekly }
public enum NormalizationMode { Off, Dynamic, FullFile }
public enum RecorderSource { Speakers, Microphone }
public enum RecorderFormat { M4A, WAV, MP3 }
public enum RecorderInsertPosition { End, Beginning }
public enum AutoKeyPressMode { PressAndHold, PressBeforeAndAfter }
public enum AutoKeyCondition { Always, ForegroundFullscreen, ListedAppRunning, ListedAppForeground }

/// <summary>Soundpad "setup modes" for routing audio into the microphone. See docs/audio-routing.md.</summary>
public enum MicSetupMode
{
    /// <summary>Try the modes below in order and pick the first that reports Good.</summary>
    Auto = 0,
    /// <summary>VB-Audio Virtual Cable ("CABLE Input" render / "CABLE Output" capture).</summary>
    VbCable = 1,
    /// <summary>VB-Audio Cable A / B (the paid extra cables).</summary>
    VbCableAB = 2,
    /// <summary>Voicemeeter virtual inputs (VAIO / AUX / VAIO3).</summary>
    Voicemeeter = 3,
    /// <summary>Any other render endpoint whose audio reappears on a capture endpoint (user-picked pair).</summary>
    GenericVirtualCable = 4,
    /// <summary>Sound card "Stereo Mix" / "What U Hear": the mix is rendered on the real speakers and captured by Stereo Mix.</summary>
    StereoMix = 5,
    /// <summary>No microphone injection; speakers only.</summary>
    SpeakersOnly = 6,
}

public sealed class AutoKeyEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Hotkey? Key { get; set; }
    public bool Enabled { get; set; } = true;
    public AutoKeyPressMode PressMode { get; set; } = AutoKeyPressMode.PressAndHold;
    public bool OnlyIfPlayedByHotkey { get; set; } = true;
    public AutoKeyCondition Condition { get; set; } = AutoKeyCondition.Always;
    public List<string> Apps { get; set; } = new();
}

public sealed class SpecialHotkeys
{
    public Hotkey? Stop { get; set; }
    public Hotkey? Start { get; set; }
    public Hotkey? Pause { get; set; }
    public Hotkey? PlayPrevious { get; set; }
    public Hotkey? PlayNext { get; set; }
    public Hotkey? PlaySelected { get; set; }
    public Hotkey? PlayRandomInCategory { get; set; }
    public Hotkey? PlayRandomFromAll { get; set; }
    public Hotkey? SelectPreviousFile { get; set; }
    public Hotkey? SelectNextFile { get; set; }
    public Hotkey? SelectPreviousCategory { get; set; }
    public Hotkey? SelectNextCategory { get; set; }
    public Hotkey? StartRecording { get; set; }
    public Hotkey? StopRecording { get; set; }
    public Hotkey? JumpBackward { get; set; }
    public Hotkey? JumpForward { get; set; }
    public Hotkey? SpeakerVolumeUp { get; set; }
    public Hotkey? SpeakerVolumeDown { get; set; }
    public Hotkey? SpeakerVolumeSet { get; set; }
    public Hotkey? SpeakerVolumeMute { get; set; }
    public Hotkey? SetPlayModeBoth { get; set; }
    public Hotkey? SetPlayModeMic { get; set; }
    public Hotkey? SetPlayModeSpeakers { get; set; }
    public Hotkey? CyclePlayMode { get; set; }
    public Hotkey? EnableHotkeys { get; set; }
    public Hotkey? DisableHotkeys { get; set; }
    public Hotkey? ToggleHotkeys { get; set; }
    public Hotkey? EnableAutoKeys { get; set; }
    public Hotkey? DisableAutoKeys { get; set; }
    public Hotkey? ToggleAutoKeys { get; set; }
    public Hotkey? PlayTts { get; set; }

    public IEnumerable<(string name, Hotkey hotkey)> All()
    {
        foreach (var p in typeof(SpecialHotkeys).GetProperties())
            if (p.PropertyType == typeof(Hotkey) && p.GetValue(this) is Hotkey h) yield return (p.Name, h);
    }
}

/// <summary>All user preferences with Soundpad-matching defaults. Serialised to %APPDATA%\VoxPad\preferences.json.</summary>
public sealed class Preferences
{
    public int Version { get; set; } = 1;

    // ---- Devices ----
    public MicSetupMode SetupMode { get; set; } = MicSetupMode.Auto;
    /// <summary>Capture (physical microphone) endpoint ids with the "Use" box ticked. Empty = default communications device.</summary>
    public List<string> MicrophoneDeviceIds { get; set; } = new();
    /// <summary>Render endpoint id of the virtual cable input (GenericVirtualCable mode), or empty for auto-detect.</summary>
    public string VirtualCableRenderId { get; set; } = "";
    /// <summary>Playback endpoint id for the speaker copy; empty = default device.</summary>
    public string SpeakerDeviceId { get; set; } = "";
    public string RecorderMicDeviceId { get; set; } = "";
    public int MicLatencyMs { get; set; } = 30;

    // ---- Audio ----
    public NormalizationMode Normalization { get; set; } = NormalizationMode.Dynamic;
    /// <summary>Voice level target in Soundpad's dB scale (LUFS + 112). Measured by the voice-measurement tool.</summary>
    public double NormalizationTargetDb { get; set; } = 74;
    public bool VoiceMeasured { get; set; }
    public bool BlockVoice { get; set; }
    public bool VoiceActivationSound { get; set; }
    public string VoiceActivationFile { get; set; } = "";
    public bool VoiceActivationOnHeadphones { get; set; }
    public bool AllowResumeInOtherPlayMode { get; set; }
    public PlayMode PlayMode { get; set; } = PlayMode.Both;
    public bool Repeat { get; set; }
    public bool Continue { get; set; }
    public bool AutoStop { get; set; } = true;
    public double SpeakerVolume { get; set; } = 1.0;
    public bool SpeakerMuted { get; set; }

    // ---- Hotkeys ----
    public bool HotkeysEnabled { get; set; } = true;
    public bool PassHotkeys { get; set; }
    public bool AllowLeftRightModifiers { get; set; }
    public bool SameHotkeysInAllCategories { get; set; }
    public bool NumpadHotkeysEnabled { get; set; } = true;
    public HotkeyModifiers NumpadModifier { get; set; } = HotkeyModifiers.Alt;
    public bool IndexHotkeysEnabled { get; set; } = true;
    public HotkeyModifiers IndexModifier { get; set; } = HotkeyModifiers.Alt;
    public int JumpSeconds { get; set; } = 5;
    public int VolumeStepPercent { get; set; } = 10;
    public int VolumeSetPercent { get; set; } = 50;
    public SpecialHotkeys Special { get; set; } = new();

    // ---- Auto Keys ----
    public bool AutoKeysEnabled { get; set; } = true;
    public List<AutoKeyEntry> AutoKeys { get; set; } = new();

    // ---- Recorder ----
    public RecorderSource RecorderSource { get; set; } = RecorderSource.Speakers;
    public RecorderFormat RecorderFormat { get; set; } = RecorderFormat.M4A;
    public RecorderInsertPosition RecorderInsert { get; set; } = RecorderInsertPosition.End;
    public bool RecorderAutoNormalize { get; set; } = true;
    public double RecorderNormalizeTargetDb { get; set; } = 89;
    public bool RecorderTrimSilence { get; set; } = true;
    public int RecorderMaxSeconds { get; set; } = 300;
    public string RecorderDirectory { get; set; } = "";
    public bool RecorderNotificationSound { get; set; } = true;

    // ---- Interface ----
    public string Language { get; set; } = "";
    public UiTheme Theme { get; set; } = UiTheme.Light;
    public List<string> ToolbarItems { get; set; } = new() { "Play", "PlaySpeakers", "PlayMic", "Pause", "Stop", "Previous", "Next", "Timer", "Seek", "Volume", "MicLevel" };
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; }
    public bool DisableSorting { get; set; }
    public bool ConfirmRemove { get; set; } = true;
    public bool ConfirmResetPlayCount { get; set; } = true;
    public bool ConfirmQuitWhilePlaying { get; set; }
    public IconSize CategoryIconSize { get; set; } = IconSize.Small;
    public bool ShowCategories { get; set; } = true;
    public bool ShowHotbar { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public List<string> VisibleColumns { get; set; } = new() { "Index", "Tag", "Duration", "Hotkey", "PlayCount", "LastPlayed" };
    public List<string> ColumnOrder { get; set; } = new() { "Index", "Tag", "Duration", "Hotkey", "PlayCount", "LastPlayed" };
    public Dictionary<string, double> ColumnWidths { get; set; } = new();
    public string SortColumn { get; set; } = "";
    public bool SortDescending { get; set; }
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 700;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }
    public double CategoriesWidth { get; set; } = 200;
    public double HotbarHeight { get; set; } = 140;
    public bool HideLicenseeName { get; set; }

    // ---- Notifications ----
    public bool NotifyMissingFiles { get; set; } = true;
    public bool NotifyDeviceChanges { get; set; } = true;
    public bool NotifyOnTrayMinimize { get; set; } = true;
    public bool NotifyRecordingStart { get; set; } = true;
    public bool NotifyTrialLimit { get; set; } = true;
    public bool NotifyUpdates { get; set; } = true;

    // ---- Updates ----
    public UpdateCheckInterval UpdateCheck { get; set; } = UpdateCheckInterval.Never;
    public string UpdateManifestUrl { get; set; } = "https://example.invalid/voxpad/update.json";
    public DateTime? LastUpdateCheck { get; set; }

    // ---- State ----
    public string CurrentListPath { get; set; } = "";
    public List<string> RecentLists { get; set; } = new();
    public string LastImportDirectory { get; set; } = "";
    public bool FirstRunDone { get; set; }

    // ---- Persistence ----

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new HotkeyJsonConverter(), new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Window placement defaults to NaN ("not placed yet"); JSON needs the named literal for that.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static Preferences Load(string? path = null)
    {
        path ??= AppPaths.PreferencesFile;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), Options) ?? new Preferences();
        }
        catch (Exception ex) { Logging.Log.Error("Preferences load failed, using defaults", ex); }
        return new Preferences();
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.PreferencesFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Export > Preferences: same JSON, different location. Device ids and window placement are stripped as they are machine-specific.</summary>
    public void Export(string path)
    {
        var copy = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(this, Options), Options)!;
        copy.MicrophoneDeviceIds.Clear();
        copy.VirtualCableRenderId = copy.SpeakerDeviceId = copy.RecorderMicDeviceId = "";
        copy.WindowLeft = copy.WindowTop = double.NaN;
        copy.CurrentListPath = "";
        copy.RecentLists.Clear();
        File.WriteAllText(path, JsonSerializer.Serialize(copy, Options));
    }

    public static Preferences Import(string path) => JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), Options) ?? new Preferences();

    public void AddRecent(string listPath)
    {
        RecentLists.RemoveAll(r => string.Equals(r, listPath, StringComparison.OrdinalIgnoreCase));
        RecentLists.Insert(0, listPath);
        while (RecentLists.Count > 10) RecentLists.RemoveAt(RecentLists.Count - 1);
    }
}
