using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoxPad.App.Services;
using VoxPad.App.ViewModels;
using VoxPad.Audio.Devices;
using VoxPad.Core.Licensing;
using VoxPad.Core.Models;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.App.Views;

/// <summary>Preferences: Devices · Audio · Hotkeys (General, Special, Numpad, Auto Keys, Advanced) · Recorder · Interface · Notifications · Updates.</summary>
public sealed class PreferencesWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Preferences _p;
    private readonly TabControl _tabs = new();
    private readonly List<Action> _apply = new();
    private bool _themeChanged, _languageChanged;

    public PreferencesWindow(MainViewModel vm, string? tab)
    {
        _vm = vm; _p = vm.Prefs;
        Title = Strings.Get("Preferences", "Preferences"); Width = 720; Height = 600; MinWidth = 600; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource("ThemedWindow");
        var dock = new DockPanel { Margin = new Thickness(10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = Strings.Get("OK", "OK"), MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = Strings.Get("Cancel", "Cancel"), MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        var apply = new Button { Content = Strings.Get("Apply", "Apply"), MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };
        ok.Click += (_, _) => { Apply(); Close(); };
        apply.Click += (_, _) => Apply();
        buttons.Children.Add(ok); buttons.Children.Add(cancel); buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(_tabs);
        Content = dock;

        AddTab("Devices", Strings.Get("Tab_Devices", "Devices"), BuildDevices());
        AddTab("Audio", Strings.Get("Tab_Audio", "Audio"), BuildAudio());
        AddTab("Hotkeys", Strings.Get("Tab_Hotkeys", "Hotkeys"), BuildHotkeys());
        AddTab("Recorder", Strings.Get("Tab_Recorder", "Recorder"), BuildRecorder());
        AddTab("Interface", Strings.Get("Tab_Interface", "Interface"), BuildInterface());
        AddTab("Notifications", Strings.Get("Tab_Notifications", "Notifications"), BuildNotifications());
        AddTab("Updates", Strings.Get("Tab_Updates", "Updates"), BuildUpdates());
        if (tab != null)
        {
            var t = _tabs.Items.OfType<TabItem>().FirstOrDefault(x => (string)x.Tag == tab || (tab == "AutoKeys" && (string)x.Tag == "Hotkeys"));
            if (t != null) _tabs.SelectedItem = t;
            if (tab == "AutoKeys" && _hotkeyTabs != null) _hotkeyTabs.SelectedIndex = 3;
        }
    }

    private void AddTab(string tag, string header, UIElement content)
        => _tabs.Items.Add(new TabItem { Header = header, Tag = tag, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10) } });

    /// <summary>Everything that needs the audio outputs reopened. Compared before/after Apply so only a real device change restarts them.</summary>
    private string DeviceSignature()
        => string.Join("|", _p.SetupMode, _p.VirtualCableRenderId, _p.SpeakerDeviceId, _p.MicLatencyMs, string.Join(",", _p.MicrophoneDeviceIds.OrderBy(x => x, StringComparer.Ordinal)));

    private void Apply()
    {
        var devicesBefore = DeviceSignature();
        foreach (var a in _apply) a();
        _p.Save();
        _vm.RebuildBindings();
        if (DeviceSignature() != devicesBefore) { _vm.ApplyDevices(); RefreshDeviceStatus(); }
        else { _vm.Engine.Mic.BlockVoice = _p.BlockVoice; _vm.Engine.Normalizer.TargetLufs = Core.Loudness.EbuR128Meter.DbToLufs(_p.NormalizationTargetDb); }
        if (_themeChanged) { ThemeService.Apply(_p.Theme); _themeChanged = false; }
        if (_languageChanged) { _vm.Dialogs.Info("VoxPad", Strings.Get("LanguageRestart", "The language changes after VoxPad is restarted.")); _languageChanged = false; }
        _vm.Status(Strings.Get("PrefsSaved", "Preferences saved"));
    }

    // ---------------------------------------------------------------- helpers

    private static TextBlock Heading(string s) => new() { Text = s, Style = (Style)Application.Current.FindResource("Heading") };
    private static TextBlock Hint(string s) => new() { Text = s, Style = (Style)Application.Current.FindResource("Hint") };
    private CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value };
        _apply.Add(() => set(c.IsChecked == true));
        return c;
    }
    private DockPanel Labeled(string label, FrameworkElement el, double labelWidth = 160)
    {
        var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        d.Children.Add(new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center });
        d.Children.Add(el);
        return d;
    }
    private TextBox Num(double value, Action<double> set, double width = 70)
    {
        var t = new TextBox { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        _apply.Add(() => { if (double.TryParse(t.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || double.TryParse(t.Text, out v)) set(v); });
        return t;
    }
    private ComboBox Combo<T>(IList<(T value, string text)> items, T current, Action<T> set, double width = 260)
    {
        int idx = items.ToList().FindIndex(i => Equals(i.value, current));
        if (idx < 0 && current is string saved && saved.Length > 0)
        {
            // The saved device is unplugged/disabled: keep it selected (and saved) instead of falling back to the first entry.
            var name = _vm.Devices.RenderDevices.Concat(_vm.Devices.CaptureDevices).FirstOrDefault(d => d.Id == saved)?.Name ?? saved;
            items = items.Append((current, name + " " + Strings.Get("NotAvailable", "[not available]"))).ToList();
            idx = items.Count - 1;
        }
        var c = new ComboBox { ItemsSource = items.Select(i => i.text).ToList(), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        c.SelectedIndex = idx < 0 ? 0 : idx;
        _apply.Add(() => { if (c.SelectedIndex >= 0 && c.SelectedIndex < items.Count) set(items[c.SelectedIndex].value); });
        return c;
    }

    private Button HotkeyButton(Func<Hotkey?> get, Action<Hotkey?> set, string title)
    {
        var b = new Button { Content = get()?.ToString() ?? Strings.Get("None", "(none)"), MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) =>
        {
            var (ok, hk) = _vm.Dialogs.CaptureHotkey(title, get());
            if (!ok) return;
            if (hk != null && !hk.IsAllowed) { _vm.Dialogs.Warn("VoxPad", Strings.Get("BareMouseNotAllowed", "Left and right mouse buttons need a modifier key.")); return; }
            set(hk);
            b.Content = hk?.ToString() ?? Strings.Get("None", "(none)");
        };
        return b;
    }

    // ---------------------------------------------------------------- Devices

    private TextBlock _deviceStatus = new();
    private StackPanel _micList = new();
    private readonly HashSet<string> _useMics = new();

    private UIElement BuildDevices()
    {
        var col = new StackPanel();
        _useMics.UnionWith(_p.MicrophoneDeviceIds);
        col.Children.Add(Heading(Strings.Get("SetupMode", "Setup mode")));
        var modes = Enum.GetValues<MicSetupMode>().Select(m => (m, DeviceManager.ModeName(m))).ToList();
        var modeCombo = Combo(modes, _p.SetupMode, m => { if (_p.SetupMode != m) { _p.SetupMode = m; } }, 320);
        col.Children.Add(modeCombo);
        col.Children.Add(Hint(Strings.Get("SetupModeHint", "VoxPad has no kernel audio driver of its own. It renders voice + sound into a virtual cable's playback endpoint; apps then record from the cable's recording endpoint (e.g. 'CABLE Output'). Auto-select tries VB-CABLE, VB-CABLE A/B, Voicemeeter, a manually chosen cable and Stereo Mix in that order.")));

        var statusRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var refresh = new Button { Content = Strings.Get("Refresh", "Refresh"), MinWidth = 80 };
        refresh.Click += (_, _) => { foreach (var a in _apply) a(); _vm.ApplyDevices(); RebuildMicList(); RefreshDeviceStatus(); };
        DockPanel.SetDock(refresh, Dock.Right);
        statusRow.Children.Add(refresh);
        statusRow.Children.Add(new TextBlock { Text = Strings.Get("Status", "Status: "), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        _deviceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        statusRow.Children.Add(_deviceStatus);
        col.Children.Add(statusRow);
        RefreshDeviceStatus();

        col.Children.Add(Heading(Strings.Get("Microphones", "Microphones (tick 'Use' for the ones to mix into the cable)")));
        _micList = new StackPanel();
        RebuildMicList();
        col.Children.Add(_micList);
        _apply.Add(() =>
        {
            var ids = _useMics.ToList();
            if (!ids.OrderBy(x => x).SequenceEqual(_p.MicrophoneDeviceIds.OrderBy(x => x))) { _p.MicrophoneDeviceIds = ids; }
        });

        col.Children.Add(Heading(Strings.Get("VirtualCableEndpoint", "Virtual cable playback endpoint (for 'Other virtual cable')")));
        var renders = new List<(string, string)> { ("", Strings.Get("AutoDetect", "(auto-detect)")) };
        renders.AddRange(_vm.Devices.RenderDevices.Where(d => d.IsActive).Select(d => (d.Id, d.Name)));
        col.Children.Add(Combo(renders, _p.VirtualCableRenderId, id => { if (_p.VirtualCableRenderId != id) { _p.VirtualCableRenderId = id; } }, 400));

        col.Children.Add(Heading(Strings.Get("SpeakerDevice", "Speakers (your own copy of the sound)")));
        var spk = new List<(string, string)> { ("", Strings.Get("DefaultDevice", "(Windows default)")) };
        spk.AddRange(_vm.Devices.RenderDevices.Where(d => d.IsActive && !DeviceManager.LooksLikeVirtualCable(d.Name)).Select(d => (d.Id, d.Name)));
        col.Children.Add(Combo(spk, _p.SpeakerDeviceId, id => { if (_p.SpeakerDeviceId != id) { _p.SpeakerDeviceId = id; } }, 400));

        col.Children.Add(Heading(Strings.Get("RecorderMic", "Microphone for the recorder")));
        var caps = new List<(string, string)> { ("", Strings.Get("DefaultDevice", "(Windows default)")) };
        caps.AddRange(_vm.Devices.CaptureDevices.Where(d => d.IsActive).Select(d => (d.Id, d.Name)));
        col.Children.Add(Combo(caps, _p.RecorderMicDeviceId, id => _p.RecorderMicDeviceId = id, 400));

        col.Children.Add(Labeled(Strings.Get("MicLatency", "Microphone latency (ms):"), Num(_p.MicLatencyMs, v => { var n = (int)Math.Clamp(v, 10, 200); if (n != _p.MicLatencyMs) { _p.MicLatencyMs = n; } })));
        col.Children.Add(Hint(Strings.Get("LatencyHint", "Lower = less delay on your voice, higher = fewer dropouts on slow machines. 30 ms is a good default.")));
        var privacy = DeviceManager.CheckMicrophonePrivacy();
        if (privacy != DeviceManager.MicPrivacy.Allowed && privacy != DeviceManager.MicPrivacy.Unknown)
        {
            var b = new Button { Content = Strings.Get("OpenPrivacySettings", "Microphone access is denied – open Windows privacy settings"), HorizontalAlignment = HorizontalAlignment.Left, Foreground = (Brush)Application.Current.FindResource("Theme.Bad") };
            b.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); } catch { } };
            col.Children.Add(b);
        }
        return col;
    }

    private void RebuildMicList()
    {
        _micList.Children.Clear();
        foreach (var d in _vm.Devices.CaptureDevices.Where(d => !DeviceManager.LooksLikeVirtualCable(d.Name) || d.IsActive))
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var use = new CheckBox { Content = Strings.Get("Use", "Use"), IsChecked = _useMics.Contains(d.Id), Width = 60, VerticalAlignment = VerticalAlignment.Center, IsEnabled = d.IsActive };
            use.Click += (_, _) => { if (use.IsChecked == true) _useMics.Add(d.Id); else _useMics.Remove(d.Id); };
            row.Children.Add(use);
            var state = d.IsActive ? (d.IsDefaultCommunications ? Strings.Get("DefaultComm", " (default communications)") : "") : $" [{d.State}]";
            row.Children.Add(new TextBlock { Text = d.Name + state, VerticalAlignment = VerticalAlignment.Center, Foreground = d.IsActive ? (Brush)Application.Current.FindResource("Theme.Foreground") : (Brush)Application.Current.FindResource("Theme.SecondaryForeground") });
            _micList.Children.Add(row);
        }
        if (_micList.Children.Count == 0) _micList.Children.Add(Hint(Strings.Get("NoMics", "No recording devices found.")));
        _micList.Children.Add(Hint(Strings.Get("NoMicsTickedHint", "Nothing ticked = the default communications microphone is used.")));
    }

    private void RefreshDeviceStatus()
    {
        var r = _vm.Engine.Routing;
        _deviceStatus.Text = r.Message;
        _deviceStatus.Foreground = (Brush)Application.Current.FindResource(r.Level == DeviceStatusLevel.Good ? "Theme.Good" : r.Level == DeviceStatusLevel.Warning ? "Theme.Warn" : "Theme.Bad");
    }

    // ---------------------------------------------------------------- Audio

    private UIElement BuildAudio()
    {
        var col = new StackPanel();
        col.Children.Add(Heading(Strings.Get("Normalization", "Volume normalization (EBU R128, per channel)")));
        var off = new RadioButton { Content = Strings.Get("Norm_Off", "Off"), IsChecked = _p.Normalization == NormalizationMode.Off };
        var dyn = new RadioButton { Content = Strings.Get("Norm_Dynamic", "Dynamic – analyse the first 6 s, refine during playback (default)"), IsChecked = _p.Normalization == NormalizationMode.Dynamic };
        var full = new RadioButton { Content = Strings.Get("Norm_Full", "Full file – analyse the whole file before playing (cached)"), IsChecked = _p.Normalization == NormalizationMode.FullFile };
        col.Children.Add(off); col.Children.Add(dyn); col.Children.Add(full);
        _apply.Add(() => _p.Normalization = off.IsChecked == true ? NormalizationMode.Off : full.IsChecked == true ? NormalizationMode.FullFile : NormalizationMode.Dynamic);
        var targetRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var measure = new Button { Content = Strings.Get("MeasureVoice", "Measure voice…"), Margin = new Thickness(8, 0, 0, 0) };
        var targetBox = Num(_p.NormalizationTargetDb, v => _p.NormalizationTargetDb = Math.Clamp(v, 40, 110));
        measure.Click += (_, _) => { new VoiceMeasureDialog(_vm) { Owner = this }.ShowDialog(); targetBox.Text = _p.NormalizationTargetDb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture); };
        targetRow.Children.Add(new TextBlock { Text = Strings.Get("Target", "Target (dB):"), Width = 160, VerticalAlignment = VerticalAlignment.Center });
        targetRow.Children.Add(targetBox); targetRow.Children.Add(measure);
        col.Children.Add(targetRow);
        col.Children.Add(Hint(Strings.Get("NormHint", "Sounds on the microphone are brought to your voice level (measure it; a good result is above 74 dB). The speaker copy is not changed – use the speaker volume slider for yourself.")));

        col.Children.Add(Heading(Strings.Get("BlockVoice", "Block voice")));
        col.Children.Add(Check(Strings.Get("BlockVoiceCheck", "Replace my voice entirely while a sound plays (instead of mixing)"), _p.BlockVoice, v => _p.BlockVoice = v));

        col.Children.Add(Heading(Strings.Get("VoiceActivation", "Voice activation sound")));
        col.Children.Add(Check(Strings.Get("VoiceActivationCheck", "Play a short loud pre-roll on the microphone so voice-activated apps open their gate"), _p.VoiceActivationSound, v => _p.VoiceActivationSound = v));
        var fileRow = new DockPanel();
        var browse = new Button { Content = "…", Width = 28, Margin = new Thickness(4, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right);
        var clear = new Button { Content = Strings.Get("Default", "Default"), Margin = new Thickness(4, 0, 0, 0) }; DockPanel.SetDock(clear, Dock.Right);
        var file = new TextBox { Text = _p.VoiceActivationFile };
        browse.Click += (_, _) => { var f = _vm.Dialogs.OpenFiles(Audio.Decoding.AudioFileDecoder.FileDialogFilter, false); if (f.Length == 1) file.Text = f[0]; };
        clear.Click += (_, _) => file.Text = "";
        fileRow.Children.Add(browse); fileRow.Children.Add(clear); fileRow.Children.Add(file);
        _apply.Add(() => _p.VoiceActivationFile = file.Text.Trim());
        col.Children.Add(Labeled(Strings.Get("CustomFile", "Custom file (empty = 250 ms tone):"), fileRow, 220));
        col.Children.Add(Check(Strings.Get("VoiceActivationHeadphones", "Also play the pre-roll on my speakers/headphones"), _p.VoiceActivationOnHeadphones, v => _p.VoiceActivationOnHeadphones = v));

        col.Children.Add(Heading(Strings.Get("Playback", "Playback")));
        col.Children.Add(Check(Strings.Get("AllowResumeOtherMode", "Allow resume in another play mode (restart at the paused position with the new mode)"), _p.AllowResumeInOtherPlayMode, v => _p.AllowResumeInOtherPlayMode = v));
        return col;
    }

    // ---------------------------------------------------------------- Hotkeys

    private TabControl? _hotkeyTabs;

    private UIElement BuildHotkeys()
    {
        _hotkeyTabs = new TabControl();
        UIElement Wrap(UIElement e) => new ScrollViewer { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };

        // General
        var gen = new StackPanel();
        gen.Children.Add(Check(Strings.Get("HotkeysEnabledCheck", "Hotkeys enabled"), _p.HotkeysEnabled, v => _p.HotkeysEnabled = v));
        gen.Children.Add(Check(Strings.Get("PassHotkeysCheck", "Pass hotkeys to the active application (default off: the key is swallowed)"), _p.PassHotkeys, v => _p.PassHotkeys = v));
        gen.Children.Add(Check(Strings.Get("AutoStopCheck", "Auto stop: sounds started by hotkey ignore Repeat and Continue (default on)"), _p.AutoStop, v => { _p.AutoStop = v; _vm.AutoStop = v; }));
        gen.Children.Add(Hint(Strings.Get("HotkeysGeneralHint", "Set a sound's hotkey by right-clicking it or double-clicking its Hotkey cell. Single keys, any combination with Ctrl/Alt/Shift/Win and mouse buttons Mid/4/5 work globally.")));
        _hotkeyTabs.Items.Add(new TabItem { Header = Strings.Get("General", "General"), Content = Wrap(gen) });

        // Special
        var special = new StackPanel();
        special.Children.Add(Hint(Strings.Get("SpecialHint", "Special hotkeys are reserved: a sound cannot take the same key.")));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        int r = 0;
        foreach (var name in MainViewModel.SpecialNames)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var prop = typeof(SpecialHotkeys).GetProperty(name)!;
            var label = new TextBlock { Text = MainViewModel.SpecialDisplayName(name), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 12, 2) };
            Grid.SetRow(label, r); grid.Children.Add(label);
            var btn = HotkeyButton(() => (Hotkey?)prop.GetValue(_p.Special), hk => prop.SetValue(_p.Special, hk), MainViewModel.SpecialDisplayName(name));
            btn.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(btn, r); Grid.SetColumn(btn, 1); grid.Children.Add(btn);
            r++;
        }
        special.Children.Add(grid);
        _hotkeyTabs.Items.Add(new TabItem { Header = Strings.Get("Special", "Special"), Content = Wrap(special) });

        // Numpad / index
        var num = new StackPanel();
        var modList = new List<(HotkeyModifiers, string)> { (HotkeyModifiers.Alt, "Alt"), (HotkeyModifiers.Ctrl, "Ctrl"), (HotkeyModifiers.Shift, "Shift"), (HotkeyModifiers.Win, "Win"), (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Ctrl+Alt"), (HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift") };
        num.Children.Add(Heading(Strings.Get("NumpadHotkeys", "Numpad hotkeys")));
        num.Children.Add(Check(Strings.Get("NumpadEnabledCheck", "Enabled: hold the modifier, type the sound's number on the numpad, release"), _p.NumpadHotkeysEnabled, v => _p.NumpadHotkeysEnabled = v));
        num.Children.Add(Labeled(Strings.Get("Modifier", "Modifier:"), Combo(modList, _p.NumpadModifier, m => _p.NumpadModifier = m, 160)));
        num.Children.Add(Hint(Strings.Get("NumpadHint", "The number is the # column of the current category.")));
        num.Children.Add(Heading(Strings.Get("IndexHotkeys", "Index hotkeys")));
        num.Children.Add(Check(Strings.Get("IndexEnabledCheck", "Enabled: hold the modifier, type the row index with the number row, release"), _p.IndexHotkeysEnabled, v => _p.IndexHotkeysEnabled = v));
        num.Children.Add(Labeled(Strings.Get("Modifier", "Modifier:"), Combo(modList, _p.IndexModifier, m => _p.IndexModifier = m, 160)));
        num.Children.Add(Hint(Strings.Get("IndexHint", "The index is the row in the hidden 'All sounds' category, e.g. Alt+1,2,3 plays row 123.")));
        _hotkeyTabs.Items.Add(new TabItem { Header = Strings.Get("Numpad", "Numpad"), Content = Wrap(num) });

        // Auto Keys
        _hotkeyTabs.Items.Add(new TabItem { Header = Strings.Get("AutoKeys", "Auto Keys"), Content = Wrap(BuildAutoKeys()) });

        // Advanced
        var adv = new StackPanel();
        adv.Children.Add(Check(Strings.Get("AllowLeftRight", "Allow left/right modifiers (LCtrl and RCtrl are different keys)"), _p.AllowLeftRightModifiers, v => _p.AllowLeftRightModifiers = v));
        adv.Children.Add(Check(Strings.Get("SameHotkeysAllCategories", "Same hotkeys in all categories (row-based: the hotkey of row N plays row N of the selected category)"), _p.SameHotkeysInAllCategories, v => _p.SameHotkeysInAllCategories = v));
        adv.Children.Add(Labeled(Strings.Get("JumpSeconds", "Jump ±N seconds:"), Num(_p.JumpSeconds, v => _p.JumpSeconds = (int)Math.Clamp(v, 1, 600))));
        adv.Children.Add(Labeled(Strings.Get("VolumeStep", "Volume up/down step (%):"), Num(_p.VolumeStepPercent, v => _p.VolumeStepPercent = (int)Math.Clamp(v, 1, 100))));
        adv.Children.Add(Labeled(Strings.Get("VolumeSet", "'Set volume' hotkey level (%):"), Num(_p.VolumeSetPercent, v => _p.VolumeSetPercent = (int)Math.Clamp(v, 0, 100))));
        _hotkeyTabs.Items.Add(new TabItem { Header = Strings.Get("Advanced", "Advanced"), Content = Wrap(adv) });
        return _hotkeyTabs;
    }

    private UIElement BuildAutoKeys()
    {
        var col = new StackPanel();
        col.Children.Add(Check(Strings.Get("AutoKeysEnabledCheck", "Auto Keys enabled"), _p.AutoKeysEnabled, v => { _p.AutoKeysEnabled = v; _vm.AutoKeysEnabled = v; }));
        col.Children.Add(Hint(Strings.Get("AutoKeysHint", "Automatic push-to-talk: when a sound starts on the microphone the key is pressed (held for the sound's duration, or tapped before and after). Only fires in microphone play modes.")));
        var list = new StackPanel();
        var entries = _p.AutoKeys.Select(e => new AutoKeyEntry { Id = e.Id, Key = e.Key, Enabled = e.Enabled, PressMode = e.PressMode, OnlyIfPlayedByHotkey = e.OnlyIfPlayedByHotkey, Condition = e.Condition, Apps = e.Apps.ToList() }).ToList();
        void Rebuild()
        {
            list.Children.Clear();
            foreach (var e in entries)
            {
                var entry = e;
                var box = new GroupBox { Header = entry.Key?.ToString() ?? Strings.Get("NoKey", "(no key)") };
                var inner = new StackPanel();
                var row1 = new WrapPanel();
                var btn = HotkeyButton(() => entry.Key, hk => { entry.Key = hk; box.Header = hk?.ToString() ?? Strings.Get("NoKey", "(no key)"); }, Strings.Get("AutoKey", "Auto Key"));
                btn.Margin = new Thickness(0, 0, 8, 0);
                row1.Children.Add(btn);
                var en = new CheckBox { Content = Strings.Get("Enabled", "Enabled"), IsChecked = entry.Enabled, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; en.Click += (_, _) => entry.Enabled = en.IsChecked == true; row1.Children.Add(en);
                var only = new CheckBox { Content = Strings.Get("OnlyIfHotkey", "Only if played by hotkey"), IsChecked = entry.OnlyIfPlayedByHotkey, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; only.Click += (_, _) => entry.OnlyIfPlayedByHotkey = only.IsChecked == true; row1.Children.Add(only);
                var remove = new Button { Content = Strings.Get("Remove", "Remove") }; remove.Click += (_, _) => { entries.Remove(entry); Rebuild(); }; row1.Children.Add(remove);
                inner.Children.Add(row1);
                var mode = new ComboBox { ItemsSource = new[] { Strings.Get("PressHold", "Press and hold for the sound's duration"), Strings.Get("PressBeforeAfter", "Press before and after") }, SelectedIndex = (int)entry.PressMode, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                mode.SelectionChanged += (_, _) => entry.PressMode = (AutoKeyPressMode)mode.SelectedIndex;
                inner.Children.Add(Labeled(Strings.Get("PressMode", "Press mode:"), mode, 120));
                var cond = new ComboBox { ItemsSource = new[] { Strings.Get("Cond_Always", "Always"), Strings.Get("Cond_Fullscreen", "Foreground app is fullscreen (incl. borderless)"), Strings.Get("Cond_Running", "A listed app is running"), Strings.Get("Cond_Foreground", "A listed app is in the foreground") }, SelectedIndex = (int)entry.Condition, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
                cond.SelectionChanged += (_, _) => entry.Condition = (AutoKeyCondition)cond.SelectedIndex;
                inner.Children.Add(Labeled(Strings.Get("Condition", "Condition:"), cond, 120));
                var apps = new TextBox { Text = string.Join(Environment.NewLine, entry.Apps), AcceptsReturn = true, MinHeight = 40, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = Strings.Get("AppsTip", "Process names, one per line, e.g. discord.exe") };
                apps.TextChanged += (_, _) => entry.Apps = apps.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                inner.Children.Add(Labeled(Strings.Get("Apps", "App list:"), apps, 120));
                box.Content = inner;
                list.Children.Add(box);
            }
        }
        Rebuild();
        col.Children.Add(list);
        var add = new Button { Content = Strings.Get("AddAutoKey", "Add Auto Key"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        add.Click += (_, _) => { entries.Add(new AutoKeyEntry()); Rebuild(); };
        col.Children.Add(add);
        var trouble = new Button { Content = Strings.Get("Troubleshooting", "Troubleshooting…"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        trouble.Click += (_, _) => new AutoKeysTroubleshootingWindow(_vm) { Owner = this }.ShowDialog();
        col.Children.Add(trouble);
        _apply.Add(() => _p.AutoKeys = entries.Where(e => e.Key != null).ToList());
        return col;
    }

    // ---------------------------------------------------------------- Recorder

    private UIElement BuildRecorder()
    {
        var col = new StackPanel();
        col.Children.Add(Labeled(Strings.Get("Source", "Source:"), Combo(new List<(RecorderSource, string)> { (RecorderSource.Speakers, Strings.Get("Source_Speakers", "Speakers (what you hear)")), (RecorderSource.Microphone, Strings.Get("Source_Mic", "Microphone")) }, _p.RecorderSource, v => _p.RecorderSource = v)));
        col.Children.Add(Labeled(Strings.Get("Format", "Format:"), Combo(new List<(RecorderFormat, string)> { (RecorderFormat.M4A, "M4A (AAC, default)"), (RecorderFormat.WAV, "WAV"), (RecorderFormat.MP3, "MP3") }, _p.RecorderFormat, v => _p.RecorderFormat = v)));
        col.Children.Add(Hint(Strings.Get("RecorderFormatHint", "Everything is converted to stereo 48 kHz. The editor saves in this format too.")));
        col.Children.Add(Labeled(Strings.Get("Insert", "Insert:"), Combo(new List<(RecorderInsertPosition, string)> { (RecorderInsertPosition.End, Strings.Get("InsertEnd", "At the end of the list")), (RecorderInsertPosition.Beginning, Strings.Get("InsertBeginning", "At the beginning")) }, _p.RecorderInsert, v => _p.RecorderInsert = v)));
        col.Children.Add(Check(Strings.Get("AutoNormalize", "Auto-normalize to"), _p.RecorderAutoNormalize, v => _p.RecorderAutoNormalize = v));
        col.Children.Add(Labeled(Strings.Get("TargetDb", "Target (dB, default 89 ≈ −23 LU; >99 clips):"), Num(_p.RecorderNormalizeTargetDb, v => _p.RecorderNormalizeTargetDb = Math.Clamp(v, 40, 110)), 300));
        col.Children.Add(Check(Strings.Get("TrimSilence", "Trim silence at start and end"), _p.RecorderTrimSilence, v => _p.RecorderTrimSilence = v));
        col.Children.Add(Labeled(Strings.Get("MaxTime", "Max recording time (s, 0 = unlimited):"), Num(_p.RecorderMaxSeconds, v => _p.RecorderMaxSeconds = (int)Math.Max(0, v)), 300));
        var dirRow = new DockPanel();
        var browse = new Button { Content = "…", Width = 28, Margin = new Thickness(4, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right);
        var dir = new TextBox { Text = string.IsNullOrEmpty(_p.RecorderDirectory) ? AppPaths.RecordingsDirectory : _p.RecorderDirectory };
        browse.Click += (_, _) => { var f = _vm.Dialogs.PickFolder(Strings.Get("TargetDirectory", "Target directory")); if (f != null) dir.Text = f; };
        dirRow.Children.Add(browse); dirRow.Children.Add(dir);
        _apply.Add(() => _p.RecorderDirectory = dir.Text == AppPaths.RecordingsDirectory ? "" : dir.Text);
        col.Children.Add(Labeled(Strings.Get("TargetDirectory", "Target directory:"), dirRow));
        col.Children.Add(Check(Strings.Get("NotificationSound", "Play a notification sound before capture starts"), _p.RecorderNotificationSound, v => _p.RecorderNotificationSound = v));
        col.Children.Add(Hint(Strings.Get("RecorderHotkeyHint", "Start/stop recording hotkeys are on the Hotkeys > Special tab.")));
        return col;
    }

    // ---------------------------------------------------------------- Interface

    private UIElement BuildInterface()
    {
        var col = new StackPanel();
        col.Children.Add(Labeled(Strings.Get("Language", "Language:"), Combo(Strings.Languages.Select(l => (l.code, l.name)).ToList(), _p.Language, v => { if (_p.Language != v) { _p.Language = v; _languageChanged = true; } })));
        col.Children.Add(Labeled(Strings.Get("Style", "Style:"), Combo(new List<(UiTheme, string)> { (UiTheme.Light, Strings.Get("Theme_Light", "Light")), (UiTheme.Dark, Strings.Get("Theme_Dark", "Dark")), (UiTheme.Native, Strings.Get("Theme_Native", "Native (Windows)")) }, _p.Theme, v => { if (_p.Theme != v) { _p.Theme = v; _themeChanged = true; } })));
        if (ThemeService.AccessibilityActive()) col.Children.Add(Hint(Strings.Get("NativeForced", "High contrast or a screen reader is active: the Native style is used.")));
        col.Children.Add(Heading(Strings.Get("ToolbarItems", "Toolbar items")));
        var items = new (string key, string text)[] { ("Play", Strings.Get("Play", "Play")), ("PlaySpeakers", Strings.Get("PlayOnSpeakers", "Play on speakers")), ("PlayMic", Strings.Get("PlayOnMic", "Play on microphone")), ("Pause", Strings.Get("PauseResume", "Pause")), ("Stop", Strings.Get("Stop", "Stop")), ("Previous", Strings.Get("Previous", "Previous")), ("Next", Strings.Get("Next", "Next")), ("Timer", Strings.Get("Timer", "Track timer")), ("Seek", Strings.Get("SeekSlider", "Seek slider")), ("Volume", Strings.Get("SpeakerVolume", "Speaker volume")), ("MicLevel", Strings.Get("MicLevelMeter", "Microphone level meter")) };
        var wrap = new WrapPanel();
        var checks = new List<(string key, CheckBox cb)>();
        foreach (var (key, text) in items) { var cb = new CheckBox { Content = text, IsChecked = _p.ToolbarItems.Contains(key), Width = 200 }; wrap.Children.Add(cb); checks.Add((key, cb)); }
        _apply.Add(() => _p.ToolbarItems = checks.Where(c => c.cb.IsChecked == true).Select(c => c.key).ToList());
        col.Children.Add(wrap);
        col.Children.Add(Heading(Strings.Get("Behaviour", "Behaviour")));
        col.Children.Add(Check(Strings.Get("StartMinimized", "Start minimized"), _p.StartMinimized, v => _p.StartMinimized = v));
        col.Children.Add(Check(Strings.Get("MinimizeToTrayCheck", "Minimize to the system tray"), _p.MinimizeToTray, v => _p.MinimizeToTray = v));
        col.Children.Add(Check(Strings.Get("CloseToTray", "Closing the window minimizes to the tray instead of exiting"), _p.CloseToTray, v => _p.CloseToTray = v));
        col.Children.Add(Check(Strings.Get("DisableSorting", "Disable sorting (column headers never sort; manual order only)"), _p.DisableSorting, v => { _p.DisableSorting = v; _vm.ApplySort(); }));
        col.Children.Add(Heading(Strings.Get("Confirmations", "Confirmations")));
        col.Children.Add(Check(Strings.Get("ConfirmRemoveCheck", "Confirm before removing sounds"), _p.ConfirmRemove, v => _p.ConfirmRemove = v));
        col.Children.Add(Check(Strings.Get("ConfirmResetCheck", "Confirm before resetting play counts"), _p.ConfirmResetPlayCount, v => _p.ConfirmResetPlayCount = v));
        col.Children.Add(Check(Strings.Get("ConfirmQuitCheck", "Confirm quitting while a sound plays"), _p.ConfirmQuitWhilePlaying, v => _p.ConfirmQuitWhilePlaying = v));
        if (LicenseManager.TrialLimitsEnabled)
            col.Children.Add(Check(Strings.Get("HideLicenseeName", "Hide my licensee name (streamer mode)"), _p.HideLicenseeName, v => _p.HideLicenseeName = v));
        return col;
    }

    private UIElement BuildNotifications()
    {
        var col = new StackPanel();
        col.Children.Add(Check(Strings.Get("NotifyMissing", "Warn about missing files when a list loads"), _p.NotifyMissingFiles, v => _p.NotifyMissingFiles = v));
        col.Children.Add(Check(Strings.Get("NotifyDevices", "Report audio device changes in the status bar"), _p.NotifyDeviceChanges, v => _p.NotifyDeviceChanges = v));
        col.Children.Add(Check(Strings.Get("NotifyTray", "Show a balloon when minimizing to the tray"), _p.NotifyOnTrayMinimize, v => _p.NotifyOnTrayMinimize = v));
        col.Children.Add(Check(Strings.Get("NotifyRecording", "Status message when a recording starts"), _p.NotifyRecordingStart, v => _p.NotifyRecordingStart = v));
        if (LicenseManager.TrialLimitsEnabled)
            col.Children.Add(Check(Strings.Get("NotifyTrial", "Show a dialog when a trial limit is hit"), _p.NotifyTrialLimit, v => _p.NotifyTrialLimit = v));
        col.Children.Add(Check(Strings.Get("NotifyUpdates", "Show available updates"), _p.NotifyUpdates, v => _p.NotifyUpdates = v));
        return col;
    }

    private UIElement BuildUpdates()
    {
        var col = new StackPanel();
        col.Children.Add(Labeled(Strings.Get("CheckInterval", "Check for updates:"), Combo(new List<(UpdateCheckInterval, string)> { (UpdateCheckInterval.Never, Strings.Get("Never", "Never (default)")), (UpdateCheckInterval.Daily, Strings.Get("Daily", "Daily")), (UpdateCheckInterval.Weekly, Strings.Get("Weekly", "Weekly")) }, _p.UpdateCheck, v => _p.UpdateCheck = v)));
        var url = new TextBox { Text = _p.UpdateManifestUrl };
        _apply.Add(() => _p.UpdateManifestUrl = url.Text.Trim());
        col.Children.Add(Labeled(Strings.Get("ManifestUrl", "Manifest URL:"), url));
        col.Children.Add(Hint(Strings.Get("UpdateHint", "Update packages are only installed when the manifest's signature and the package hash verify against the key built into VoxPad.")));
        col.Children.Add(new TextBlock { Text = $"{Strings.Get("LastCheck", "Last check")}: {(_p.LastUpdateCheck?.ToLocalTime().ToString("g") ?? Strings.Get("Never", "never"))}" });
        var now = new Button { Content = Strings.Get("CheckNow", "Check now"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        now.Click += (_, _) => { foreach (var a in _apply) a(); new UpdateWindow(_vm, true) { Owner = this }.Show(); };
        col.Children.Add(now);
        return col;
    }
}
