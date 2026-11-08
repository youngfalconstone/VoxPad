using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using VoxPad.App.Services;
using VoxPad.Audio.Devices;
using VoxPad.Audio.Engine;
using VoxPad.Core;
using LicenseManager = VoxPad.Core.Licensing.LicenseManager;
using VoxPad.Core.Logging;
using VoxPad.Core.Models;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.App.ViewModels;

/// <summary>State and commands of the main window. Partial: List.cs (editing/import), Hotkeys.cs (bindings, remote API).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _tick;
    private PlaybackInfo? _lastStarted;
    private bool _pausedOnMic;
    private int _playsThisSession;

    public Preferences Prefs { get; }
    public DocumentService Doc { get; }
    public DeviceManager Devices { get; }
    public SoundboardEngine Engine { get; }
    public HotkeyService Hotkeys { get; }
    public AutoKeysService AutoKeys { get; }
    public GlobalStats Stats { get; }
    public HotbarViewModel Hotbar { get; } = new();
    public IDialogService Dialogs { get; set; } = null!;

    public ObservableCollection<CategoryViewModel> CategoryRoots { get; } = new();
    public CategoryViewModel AllSoundsVm { get; private set; } = null!;
    public ObservableCollection<SoundItemViewModel> Sounds { get; } = new();
    public ICollectionView SoundsView { get; }
    public List<SoundItemViewModel> SelectedSounds { get; } = new();

    public MainViewModel(Preferences prefs, DocumentService doc, DeviceManager devices, SoundboardEngine engine, HotkeyService hotkeys, AutoKeysService autoKeys, GlobalStats stats)
    {
        Prefs = prefs; Doc = doc; Devices = devices; Engine = engine; Hotkeys = hotkeys; AutoKeys = autoKeys; Stats = stats;
        SoundsView = CollectionViewSource.GetDefaultView(Sounds);
        SoundsView.Filter = FilterSound;
        _playMode = prefs.PlayMode; _repeat = prefs.Repeat; _continue = prefs.Continue; _autoStop = prefs.AutoStop;

        Doc.Changed += () => App.Current.Dispatcher.Invoke(RebuildFromDocument);
        Doc.DirtyChanged += () => App.Current.Dispatcher.Invoke(() => Raise(nameof(WindowTitle)));
        Engine.Finished += info => App.Current.Dispatcher.BeginInvoke(() => OnFinished(info));
        // Engine callbacks arrive on the UI thread when triggered by UI/remote actions and on audio threads otherwise.
        Engine.StateChanged += () => OnUi(UpdatePlaybackState);
        Engine.Warning += msg => OnUi(() => Status(msg, true));
        Devices.DevicesChanged += () => App.Current.Dispatcher.BeginInvoke(OnDevicesChanged);
        Hotkeys.Triggered += OnHotkey;

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        CreateCommands();
        RebuildFromDocument();
    }

    private static void OnUi(Action a)
    {
        var d = App.Current.Dispatcher;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    // ------------------------------------------------------------------ status / title

    private string _statusText = "";
    private bool _statusIsWarning;
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public bool StatusIsWarning { get => _statusIsWarning; private set => Set(ref _statusIsWarning, value); }

    public void Status(string text, bool warning = false)
    {
        StatusText = text; StatusIsWarning = warning;
        if (warning) Log.Warn(text); else Log.Info("status: " + text);
    }

    public string WindowTitle
    {
        get
        {
            var name = Doc.Path ?? Strings.Get("UntitledList", "Untitled");
            return $"VoxPad – {name}{(Doc.IsDirty ? "*" : "")}{(!LicenseManager.IsLimited ? "" : "   [" + Strings.Get("Trial", "Trial") + "]")}";
        }
    }

    public bool IsTrial => LicenseManager.IsLimited;
    public string TrialText => IsTrial ? Strings.Format("TrialStatus", LicenseManager.TrialMaxSounds, LicenseManager.PlaysLeftThisLaunch) : "";
    public void RefreshLicenseState() { Raise(nameof(IsTrial)); Raise(nameof(TrialText)); Raise(nameof(WindowTitle)); }

    public string RoutingText => Engine.Routing.Message;
    public bool RoutingGood => Engine.Routing.IsGood;

    // ------------------------------------------------------------------ playback state

    private PlayMode _playMode;
    private bool _repeat, _continue, _autoStop;
    private TimeSpan _position, _duration;
    private bool _isPlaying, _isPaused, _isSeekMic;
    private double _micLevel;
    private SoundItemViewModel? _playingItem;
    private string _playingLabel = "";

    public PlayMode PlayMode
    {
        get => _playMode;
        set { if (Set(ref _playMode, value)) { Prefs.PlayMode = value; Raise(nameof(PlayModeText)); Status(Strings.Format("PlayModeSet", PlayModeText)); } }
    }
    public string PlayModeText => _playMode switch
    {
        PlayMode.MicrophoneOnly => Strings.Get("PlayMode_Mic", "Microphone"),
        PlayMode.SpeakersOnly => Strings.Get("PlayMode_Speakers", "Speakers"),
        _ => Strings.Get("PlayMode_Both", "Speakers + Mic"),
    };
    public bool Repeat { get => _repeat; set { if (Set(ref _repeat, value)) Prefs.Repeat = value; } }
    public bool Continue { get => _continue; set { if (Set(ref _continue, value)) Prefs.Continue = value; } }
    public bool AutoStop { get => _autoStop; set { if (Set(ref _autoStop, value)) Prefs.AutoStop = value; } }

    public bool HotkeysEnabled
    {
        get => Hotkeys.Enabled;
        set { Hotkeys.Enabled = value; Prefs.HotkeysEnabled = value; Raise(); Status(value ? Strings.Get("HotkeysOn", "Hotkeys enabled") : Strings.Get("HotkeysOff", "Hotkeys disabled")); }
    }
    public bool AutoKeysEnabled
    {
        get => AutoKeys.Enabled;
        set { AutoKeys.Enabled = value; Raise(); Status(value ? Strings.Get("AutoKeysOn", "Auto Keys enabled") : Strings.Get("AutoKeysOff", "Auto Keys disabled")); }
    }
    public bool PassHotkeys
    {
        get => Prefs.PassHotkeys;
        set { Prefs.PassHotkeys = value; Hotkeys.PassHotkeys = value; Raise(); }
    }

    public TimeSpan Position { get => _position; private set { if (Set(ref _position, value)) { Raise(nameof(ElapsedText)); Raise(nameof(SeekFraction)); } } }
    public TimeSpan Duration { get => _duration; private set { if (Set(ref _duration, value)) Raise(nameof(ElapsedText)); } }
    public string ElapsedText => $"{SoundItemViewModel.Format(_position)} / {SoundItemViewModel.Format(_duration)}";
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public bool IsPaused { get => _isPaused; private set => Set(ref _isPaused, value); }
    public bool IsSeekMic { get => _isSeekMic; private set => Set(ref _isSeekMic, value); }
    public double MicLevel { get => _micLevel; private set => Set(ref _micLevel, value); }
    public SoundItemViewModel? PlayingItem { get => _playingItem; private set => Set(ref _playingItem, value); }
    public string PlayingLabel { get => _playingLabel; private set => Set(ref _playingLabel, value); }

    private bool _seeking;
    public double SeekFraction
    {
        get => _duration.TotalSeconds > 0 ? _position.TotalSeconds / _duration.TotalSeconds : 0;
        set
        {
            if (_duration.TotalSeconds <= 0) return;
            _seeking = true;
            Engine.Seek(TimeSpan.FromSeconds(value * _duration.TotalSeconds));
            _seeking = false;
        }
    }

    public double SpeakerVolume
    {
        get => Prefs.SpeakerVolume;
        set
        {
            value = Math.Clamp(value, 0, 1);
            Prefs.SpeakerVolume = value;
            Engine.Speakers.Volume = (float)value;
            Raise();
        }
    }
    public bool SpeakerMuted
    {
        get => Prefs.SpeakerMuted;
        set { Prefs.SpeakerMuted = value; Engine.Speakers.Muted = value; Raise(); }
    }

    private int _tickCount;
    private void Tick()
    {
        if (_seeking) return;
        Position = Engine.Position;
        var d = Engine.Duration;
        if (d != Duration) Duration = d;
        float peak = Engine.MicPeak;
        MicLevel = Math.Max(peak, MicLevel * 0.85);
        if (++_tickCount % 10 == 0)
        {
            // Mirror Volume Mixer changes made outside the app.
            var v = Engine.Speakers.Volume;
            if (Math.Abs(v - Prefs.SpeakerVolume) > 0.01) { Prefs.SpeakerVolume = v; Raise(nameof(SpeakerVolume)); }
            var m = Engine.Speakers.Muted;
            if (m != Prefs.SpeakerMuted) { Prefs.SpeakerMuted = m; Raise(nameof(SpeakerMuted)); }
        }
    }

    private void UpdatePlaybackState()
    {
        var cur = Engine.Current;
        IsPlaying = cur != null && !Engine.IsPaused;
        IsPaused = Engine.IsPaused;
        IsSeekMic = Engine.AudibleToOthers;
        Duration = Engine.Duration;
        Position = Engine.Position;
        var id = cur?.Sound?.Id;
        PlayingLabel = cur?.Label ?? "";
        PlayingItem = id == null ? null : Sounds.FirstOrDefault(s => s.Id == id);
        foreach (var s in Sounds) s.IsPlaying = s.Id == id;
        Hotbar.MarkPlaying(id);
        RelayCommand.Requery();
    }

    // ------------------------------------------------------------------ playing

    /// <summary>Plays one entry. Handles trial limits, stats, Auto Keys and the status bar.</summary>
    public bool PlaySound(SoundEntry entry, PlayTarget? target, PlayTrigger trigger)
    {
        var t = target ?? Engine.TargetFor(PlayMode);
        bool wantsMic = t != PlayTarget.Speakers && Engine.Mic.IsRunning;
        if (wantsMic && !LicenseManager.TryConsumePlay())
        {
            Status(Strings.Get("TrialPlayLimit", "Trial: 10 microphone plays per launch reached. Register to remove the limit."), true);
            if (Prefs.NotifyTrialLimit) Dialogs.Warn("VoxPad", Strings.Get("TrialPlayLimit", "Trial limit reached"));
            RefreshLicenseState();
            return false;
        }
        AutoKeys.OnSoundStopped();
        var info = Engine.Play(entry, t, trigger);
        if (info == null) { RefreshLicenseState(); return false; }
        _lastStarted = info;
        if (info.OnMic)
        {
            entry.PlayCount++;
            entry.LastPlayed = DateTime.Now;
            Doc.List.TotalPlays++;
            Stats.AllTimePlays++; Stats.LastPlay = DateTime.Now;
            Doc.MarkDirty();
            _playsThisSession++;
            Sounds.FirstOrDefault(s => s.Id == entry.Id)?.Refresh();
            AutoKeys.OnSoundStarted(onMic: true, playedByHotkey: trigger == PlayTrigger.Hotkey);
        }
        else AutoKeys.OnSoundStarted(onMic: false, playedByHotkey: trigger == PlayTrigger.Hotkey);
        Status(Strings.Format("Playing", entry.Tag, info.OnMic && info.OnSpeakers ? Strings.Get("PlayMode_Both", "Speakers + Mic") : info.OnMic ? Strings.Get("PlayMode_Mic", "Microphone") : Strings.Get("PlayMode_Speakers", "Speakers")));
        RefreshLicenseState();
        UpdatePlaybackState();
        return true;
    }

    public void PlayBuffer(float[] pcm, string label, PlayTarget target)
    {
        bool wantsMic = target != PlayTarget.Speakers && Engine.Mic.IsRunning;
        if (wantsMic && !LicenseManager.TryConsumePlay()) { Status(Strings.Get("TrialPlayLimit", "Trial limit reached"), true); return; }
        AutoKeys.OnSoundStopped();
        var info = Engine.PlayBuffer(pcm, label, target);
        if (info != null) { _lastStarted = info; AutoKeys.OnSoundStarted(info.OnMic, false); }
        UpdatePlaybackState();
    }

    public void Stop()
    {
        Engine.Stop();
        AutoKeys.OnSoundStopped();
        _lastStarted = null;
        UpdatePlaybackState();
    }

    public void TogglePause()
    {
        if (!Engine.IsLoaded) return;
        if (Engine.IsPaused)
        {
            var cur = _lastStarted;
            if (Prefs.AllowResumeInOtherPlayMode && cur?.Sound != null && cur.Target != Engine.TargetFor(PlayMode) && cur.Trigger != PlayTrigger.Remote)
            {
                var pos = Engine.Position;
                if (PlaySound(cur.Sound, Engine.TargetFor(PlayMode), cur.Trigger)) Engine.Seek(pos);
                return;
            }
            Engine.Resume();
            if (_pausedOnMic) AutoKeys.OnSoundStarted(true, cur?.Trigger == PlayTrigger.Hotkey);
        }
        else
        {
            _pausedOnMic = Engine.AudibleToOthers;
            Engine.Pause();
            AutoKeys.OnSoundStopped();
        }
        UpdatePlaybackState();
    }

    private void OnFinished(PlaybackInfo info)
    {
        AutoKeys.OnSoundStopped();
        UpdatePlaybackState();
        bool hotkeyAutoStop = AutoStop && info.Trigger == PlayTrigger.Hotkey;
        if (info.Sound == null || hotkeyAutoStop || info.Trigger == PlayTrigger.Remote) return;
        if (Repeat) { PlaySound(info.Sound, info.Target, PlayTrigger.AutoContinue); return; }
        if (Continue)
        {
            var next = Neighbour(info.Sound.Id, +1, wrap: false);
            if (next != null) PlaySound(next.Entry, info.Target, PlayTrigger.AutoContinue);
        }
    }

    /// <summary>Row before/after the given sound in the current (sorted, filtered) view.</summary>
    private SoundItemViewModel? Neighbour(Guid id, int delta, bool wrap)
    {
        var rows = SoundsView.Cast<SoundItemViewModel>().ToList();
        if (rows.Count == 0) return null;
        int i = rows.FindIndex(r => r.Id == id);
        if (i < 0) return delta > 0 ? rows[0] : rows[^1];
        int j = i + delta;
        if (j < 0 || j >= rows.Count) { if (!wrap) return null; j = (j + rows.Count) % rows.Count; }
        return rows[j];
    }

    private SoundItemViewModel? PrimarySelection => SelectedSounds.FirstOrDefault() ?? (SoundsView.CurrentItem as SoundItemViewModel);

    public void PlaySelected(PlayTarget? target, PlayTrigger trigger = PlayTrigger.UserInterface)
    {
        var item = PrimarySelection ?? SoundsView.Cast<SoundItemViewModel>().FirstOrDefault();
        if (item == null) { Status(Strings.Get("NothingToPlay", "Nothing to play"), true); return; }
        PlaySound(item.Entry, target, trigger);
    }

    public void PlayPrevious(PlayTrigger trigger = PlayTrigger.UserInterface)
    {
        var anchor = PlayingItem ?? (_lastStarted?.Sound is { } s ? Sounds.FirstOrDefault(x => x.Id == s.Id) : PrimarySelection);
        var item = anchor == null ? SoundsView.Cast<SoundItemViewModel>().LastOrDefault() : Neighbour(anchor.Id, -1, wrap: true);
        if (item != null) { SelectOnly(item); PlaySound(item.Entry, null, trigger); }
    }

    public void PlayNext(PlayTrigger trigger = PlayTrigger.UserInterface)
    {
        var anchor = PlayingItem ?? (_lastStarted?.Sound is { } s ? Sounds.FirstOrDefault(x => x.Id == s.Id) : PrimarySelection);
        var item = anchor == null ? SoundsView.Cast<SoundItemViewModel>().FirstOrDefault() : Neighbour(anchor.Id, +1, wrap: true);
        if (item != null) { SelectOnly(item); PlaySound(item.Entry, null, trigger); }
    }

    public void PlayRandom(Category? category, PlayTrigger trigger)
    {
        var ids = category == null || category == Doc.AllSounds ? Doc.List.AllSoundsOrder : category.SoundIds;
        var candidates = ids.Select(Doc.List.Find).Where(s => s != null && !s!.IsMissing).ToList();
        if (candidates.Count == 0) { Status(Strings.Get("NothingToPlay", "Nothing to play"), true); return; }
        var pick = candidates[Random.Shared.Next(candidates.Count)]!;
        PlaySound(pick, null, trigger);
    }

    public event Action<SoundItemViewModel>? SelectionRequested;
    public void SelectOnly(SoundItemViewModel item) => SelectionRequested?.Invoke(item);

    public void SelectRelative(int delta)
    {
        var anchor = PrimarySelection;
        var item = anchor == null ? SoundsView.Cast<SoundItemViewModel>().FirstOrDefault() : Neighbour(anchor.Id, delta, wrap: false);
        if (item != null) SelectOnly(item);
    }

    public void SelectCategoryRelative(int delta)
    {
        var all = CategoryRoots.SelectMany(c => c.Flatten()).Where(c => !c.IsHidden || c.IsAllSounds).ToList();
        if (all.Count == 0) return;
        int i = SelectedCategory == null ? -1 : all.IndexOf(SelectedCategory);
        int j = Math.Clamp(i + delta, 0, all.Count - 1);
        SelectedCategory = all[j];
        all[j].IsSelected = true;
    }

    // ------------------------------------------------------------------ devices

    private void OnDevicesChanged()
    {
        if (Prefs.NotifyDeviceChanges) Status(Strings.Get("DevicesChanged", "Audio devices changed; re-applying routing."));
        Engine.ApplyDevices();
        Raise(nameof(RoutingText)); Raise(nameof(RoutingGood));
    }

    public void ApplyDevices()
    {
        Engine.ApplyDevices();
        Raise(nameof(RoutingText)); Raise(nameof(RoutingGood));
        if (!Engine.Routing.IsGood) Status(Engine.Routing.Message, Engine.Routing.Level == DeviceStatusLevel.Error);
    }

    public void Shutdown()
    {
        _tick.Stop();
        AutoKeys.ReleaseAll();
        Prefs.PlayMode = PlayMode; Prefs.Repeat = Repeat; Prefs.Continue = Continue; Prefs.AutoStop = AutoStop;
        Stats.Save();
    }
}
