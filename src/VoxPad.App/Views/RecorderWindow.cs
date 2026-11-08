using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VoxPad.App.Controls;
using VoxPad.App.Services;
using VoxPad.App.ViewModels;
using VoxPad.Audio.Decoding;
using VoxPad.Audio.Engine;
using VoxPad.Audio.Recorder;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.App.Views;

/// <summary>Recorder (Ctrl+R): speakers (what-you-hear) or microphone → M4A/WAV/MP3 at 48 kHz stereo, trimmed and normalised, added to the list.</summary>
public sealed class RecorderWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Recorder _rec;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly LevelMeter _meter;
    private readonly TextBlock _time, _status;
    private readonly Button _start, _stop;
    private readonly ComboBox _source, _format, _insert;
    private readonly CheckBox _normalize, _trim;
    private readonly TextBox _target, _maxSeconds, _dir;
    private float[]? _lastTake;

    public RecorderWindow(MainViewModel vm)
    {
        _vm = vm;
        _rec = new Recorder(vm.Devices);
        Title = Strings.Get("Recorder", "Recorder"); Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource("ThemedWindow");
        var p = vm.Prefs;
        var col = new StackPanel { Margin = new Thickness(12) };

        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        int row = 0;
        void Add(string label, FrameworkElement el)
        {
            g.RowDefinitions.Add(new RowDefinition());
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
            Grid.SetRow(l, row); g.Children.Add(l);
            Grid.SetRow(el, row); Grid.SetColumn(el, 1); el.Margin = new Thickness(0, 2, 0, 2); g.Children.Add(el);
            row++;
        }
        _source = new ComboBox { ItemsSource = new[] { Strings.Get("Source_Speakers", "Speakers (what you hear)"), Strings.Get("Source_Mic", "Microphone") }, SelectedIndex = (int)p.RecorderSource };
        Add(Strings.Get("Source", "Source:"), _source);
        _format = new ComboBox { ItemsSource = new[] { "M4A (AAC)", "WAV", "MP3" }, SelectedIndex = (int)p.RecorderFormat };
        Add(Strings.Get("Format", "Format:"), _format);
        _insert = new ComboBox { ItemsSource = new[] { Strings.Get("InsertEnd", "Add at end of list"), Strings.Get("InsertBeginning", "Insert at beginning") }, SelectedIndex = (int)p.RecorderInsert };
        Add(Strings.Get("Insert", "Insert:"), _insert);
        var normRow = new DockPanel();
        _normalize = new CheckBox { Content = Strings.Get("AutoNormalize", "Auto-normalize to"), IsChecked = p.RecorderAutoNormalize, VerticalAlignment = VerticalAlignment.Center };
        _target = new TextBox { Text = p.RecorderNormalizeTargetDb.ToString("F0"), Width = 50, Margin = new Thickness(6, 0, 4, 0) };
        normRow.Children.Add(_normalize); normRow.Children.Add(_target); normRow.Children.Add(new TextBlock { Text = Strings.Get("DbHint", "dB (89 ≈ −23 LUFS; above 99 clips)"), VerticalAlignment = VerticalAlignment.Center });
        Add("", normRow);
        _trim = new CheckBox { Content = Strings.Get("TrimSilence", "Trim silence at start and end"), IsChecked = p.RecorderTrimSilence };
        Add("", _trim);
        var maxRow = new DockPanel();
        _maxSeconds = new TextBox { Text = p.RecorderMaxSeconds.ToString(), Width = 60 };
        maxRow.Children.Add(_maxSeconds); maxRow.Children.Add(new TextBlock { Text = "  " + Strings.Get("MaxSecondsHint", "seconds (0 = unlimited; buffered uncompressed in %TEMP%)"), VerticalAlignment = VerticalAlignment.Center });
        Add(Strings.Get("MaxTime", "Max time:"), maxRow);
        var dirRow = new DockPanel();
        var browse = new Button { Content = "…", Width = 28, Margin = new Thickness(4, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        _dir = new TextBox { Text = string.IsNullOrEmpty(p.RecorderDirectory) ? AppPaths.RecordingsDirectory : p.RecorderDirectory };
        browse.Click += (_, _) => { var f = vm.Dialogs.PickFolder(Strings.Get("TargetDirectory", "Target directory")); if (f != null) _dir.Text = f; };
        dirRow.Children.Add(browse); dirRow.Children.Add(_dir);
        Add(Strings.Get("TargetDirectory", "Directory:"), dirRow);
        col.Children.Add(g);

        _meter = new LevelMeter { Height = 16, Background = (Brush)Application.Current.FindResource("Theme.MeterBackground"), Margin = new Thickness(0, 6, 0, 4) };
        col.Children.Add(_meter);
        _time = new TextBlock { Text = "0:00", FontFamily = new FontFamily("Consolas"), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
        col.Children.Add(_time);
        _status = new TextBlock { Style = (Style)Application.Current.FindResource("Hint"), Text = Strings.Format("RecorderHotkeys", HotkeyText(p.Special.StartRecording), HotkeyText(p.Special.StopRecording)) };
        col.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _start = new Button { Content = "⏺ " + Strings.Get("StartRecording", "Record"), MinWidth = 110, Margin = new Thickness(6, 0, 0, 0) };
        _stop = new Button { Content = "⏹ " + Strings.Get("StopRecording", "Stop"), MinWidth = 110, Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
        var review = new Button { Content = Strings.Get("PlayLastTake", "Play last take"), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
        _start.Click += (_, _) => Start();
        _stop.Click += (_, _) => Stop();
        review.Click += (_, _) => { if (_lastTake != null) vm.PlayBuffer(_lastTake, Strings.Get("LastTake", "Last take"), PlayTarget.Speakers); };
        buttons.Children.Add(review); buttons.Children.Add(_start); buttons.Children.Add(_stop);
        col.Children.Add(buttons);
        Content = col;

        _rec.MaxTimeReached += () => Dispatcher.BeginInvoke(() => { _status.Text = Strings.Get("MaxTimeReached", "Maximum recording time reached."); Stop(); });
        _rec.Error += msg => Dispatcher.BeginInvoke(() => { _status.Text = msg; SetRecording(false); });
        _timer.Tick += (_, _) => { if (_rec.IsRecording) { _meter.Level = _rec.Peak; _time.Text = SoundItemViewModel.Format(_rec.Elapsed); } };
        _timer.Start();
        Closing += (_, e) => { if (_rec.IsRecording) { if (!vm.Dialogs.Confirm("VoxPad", Strings.Get("RecordingInProgress", "Recording in progress. Discard it?"))) { e.Cancel = true; return; } _rec.Cancel(); } };
        Closed += (_, _) => { _timer.Stop(); _rec.Dispose(); SavePrefs(); if (_lastTake != null) _lastTake = null; };
        void SaveOnChange(object s, EventArgs _) => SavePrefs();
        _source.SelectionChanged += SaveOnChange; _format.SelectionChanged += SaveOnChange; _insert.SelectionChanged += SaveOnChange;
        _normalize.Click += SaveOnChange; _trim.Click += SaveOnChange;
    }

    private static string HotkeyText(Core.Models.Hotkey? h) => h?.ToString() ?? Strings.Get("NotSet", "not set");

    private void SavePrefs()
    {
        var p = _vm.Prefs;
        p.RecorderSource = (RecorderSource)Math.Max(0, _source.SelectedIndex);
        p.RecorderFormat = (RecorderFormat)Math.Max(0, _format.SelectedIndex);
        p.RecorderInsert = (RecorderInsertPosition)Math.Max(0, _insert.SelectedIndex);
        p.RecorderAutoNormalize = _normalize.IsChecked == true;
        p.RecorderTrimSilence = _trim.IsChecked == true;
        if (double.TryParse(_target.Text, out var t)) p.RecorderNormalizeTargetDb = t;
        if (int.TryParse(_maxSeconds.Text, out var m)) p.RecorderMaxSeconds = Math.Max(0, m);
        p.RecorderDirectory = _dir.Text == AppPaths.RecordingsDirectory ? "" : _dir.Text;
    }

    private void SetRecording(bool on)
    {
        _start.IsEnabled = !on; _stop.IsEnabled = on;
        _source.IsEnabled = _format.IsEnabled = !on;
    }

    public void StartFromHotkey() { if (!_rec.IsRecording) Start(); }
    public void StopFromHotkey() { if (_rec.IsRecording) Stop(); }

    private void Start()
    {
        SavePrefs();
        var p = _vm.Prefs;
        if (p.RecorderAutoNormalize && p.RecorderNormalizeTargetDb > 99 && !_vm.Dialogs.Confirm("VoxPad", Strings.Get("TargetTooLoud", "A target above 99 dB will clip. Continue anyway?"))) return;
        if (p.RecorderNotificationSound)
        {
            // Notification beep on the speakers before capture starts (so it is not part of a mic recording).
            _vm.Engine.Speakers.Slot.Load(MemoryWaveStream.AsSource(ToneGenerator.Burst(0.12, 880, 0.4f)), null, TimeSpan.Zero, null);
            Thread.Sleep(180);
        }
        if (_vm.Engine.IsLoaded && p.RecorderSource == RecorderSource.Microphone) _vm.Stop();
        var devId = p.RecorderSource == RecorderSource.Speakers ? p.SpeakerDeviceId : p.RecorderMicDeviceId;
        if (_rec.Start(p.RecorderSource, devId, p.RecorderMaxSeconds))
        {
            SetRecording(true);
            _status.Text = Strings.Format("RecordingFrom", _rec.SourceName ?? "");
            if (p.NotifyRecordingStart) _vm.Status(Strings.Get("RecordingStarted", "Recording started"));
        }
    }

    private void Stop()
    {
        if (!_rec.IsRecording && _rec.Elapsed == TimeSpan.Zero) return;
        var p = _vm.Prefs;
        SetRecording(false);
        float[]? pcm;
        try { pcm = _rec.Stop(p.RecorderTrimSilence, p.RecorderAutoNormalize, p.RecorderNormalizeTargetDb); }
        catch (Exception ex) { _status.Text = ex.Message; return; }
        if (pcm == null || pcm.Length == 0) { _status.Text = Strings.Get("NothingRecorded", "Nothing was recorded (silence)."); return; }
        _lastTake = pcm;
        try
        {
            var dir = string.IsNullOrEmpty(p.RecorderDirectory) ? AppPaths.RecordingsDirectory : p.RecorderDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"Recording {DateTime.Now:yyyy-MM-dd HH-mm-ss}{AudioEncoder.Extension(p.RecorderFormat)}");
            AudioEncoder.Encode(pcm, path, p.RecorderFormat);
            _vm.AddEntryFromFile(path, p.RecorderInsert == RecorderInsertPosition.Beginning);
            _status.Text = Strings.Format("RecordingSaved", path);
            _vm.Status(Strings.Format("RecordingSaved", path));
        }
        catch (Exception ex) { _status.Text = ex.Message; _vm.Dialogs.Warn("VoxPad", ex.Message); }
    }
}
