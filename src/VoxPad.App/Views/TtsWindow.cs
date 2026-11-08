using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VoxPad.App.Services;
using VoxPad.App.ViewModels;
using VoxPad.Audio.Engine;
using VoxPad.Audio.Recorder;
using VoxPad.Core.Logging;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.App.Views;

/// <summary>Text-to-speech (Ctrl+T): Enter previews on speakers, Ctrl+Enter sends to the microphone, Save adds the line to the list as a file.</summary>
public sealed class TtsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Audio.Tts.TtsEngine _tts = new();
    private readonly TextBox _text;
    private readonly ComboBox _voices;
    private readonly Slider _rate;

    public TtsWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = Strings.Get("TextToSpeech", "Text to speech"); Width = 520; Height = 300; MinWidth = 380; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource("ThemedWindow");
        var dock = new DockPanel { Margin = new Thickness(10) };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(new TextBlock { Text = Strings.Get("Voice", "Voice:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _rate = new Slider { Minimum = -10, Maximum = 10, Value = 0, Width = 100, VerticalAlignment = VerticalAlignment.Center, ToolTip = Strings.Get("Rate", "Rate") };
        DockPanel.SetDock(_rate, Dock.Right); top.Children.Add(_rate);
        var rateLabel = new TextBlock { Text = Strings.Get("Rate", "Rate"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        DockPanel.SetDock(rateLabel, Dock.Right); top.Children.Add(rateLabel);
        _voices = new ComboBox { ItemsSource = _tts.Voices, SelectedItem = _tts.CurrentVoice };
        _voices.SelectionChanged += (_, _) => _tts.SelectVoice(_voices.SelectedItem as string);
        top.Children.Add(_voices);
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Content = t, Margin = new Thickness(6, 0, 0, 0), MinWidth = 90 }; b.Click += (_, _) => a(); return b; }
        buttons.Children.Add(B(Strings.Get("PreviewSpeakers", "Preview (Enter)"), () => Speak(PlayTarget.Speakers)));
        buttons.Children.Add(B(Strings.Get("SendToMic", "Send to mic (Ctrl+Enter)"), () => Speak(PlayTarget.Both)));
        buttons.Children.Add(B(Strings.Get("SaveToList", "Save to list"), SaveToList));
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);

        var hint = new TextBlock { Text = Strings.Get("TtsHint", "Enter = preview on speakers · Ctrl+Enter = play on microphone · special hotkey 'Play TTS' speaks this text."), Style = (Style)Application.Current.FindResource("Hint") };
        DockPanel.SetDock(hint, Dock.Bottom); dock.Children.Add(hint);

        _text = new TextBox { AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14 };
        _text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Speak(Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? PlayTarget.Both : PlayTarget.Speakers); e.Handled = true; }
        };
        dock.Children.Add(_text);
        Content = dock;
        Loaded += (_, _) => _text.Focus();
        Closed += (_, _) => _tts.Dispose();
    }

    private float[]? Synth()
    {
        try { _tts.Rate = (int)_rate.Value; return _tts.Synthesize(_text.Text); }
        catch (Exception ex) { Log.Error("TTS", ex); _vm.Status("TTS: " + ex.Message, true); return null; }
    }

    private void Speak(PlayTarget target)
    {
        var pcm = Synth();
        if (pcm == null || pcm.Length == 0) return;
        _vm.PlayBuffer(pcm, "TTS: " + _text.Text, target == PlayTarget.Both ? _vm.Engine.TargetFor(_vm.PlayMode) : target);
    }

    /// <summary>Called by the 'Play TTS' special hotkey.</summary>
    public void SpeakCurrentToMic() => Speak(PlayTarget.Both);

    private void SaveToList()
    {
        var pcm = Synth();
        if (pcm == null || pcm.Length == 0) return;
        try
        {
            var dir = string.IsNullOrEmpty(_vm.Prefs.RecorderDirectory) ? AppPaths.RecordingsDirectory : _vm.Prefs.RecorderDirectory;
            var stem = new string(_text.Text.Take(40).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            if (stem.Length == 0) stem = "tts";
            var path = Path.Combine(dir, $"TTS {stem}{AudioEncoder.Extension(_vm.Prefs.RecorderFormat)}");
            int n = 1; while (File.Exists(path)) path = Path.Combine(dir, $"TTS {stem} ({n++}){AudioEncoder.Extension(_vm.Prefs.RecorderFormat)}");
            AudioEncoder.Encode(pcm, path, _vm.Prefs.RecorderFormat);
            _vm.AddEntryFromFile(path, _vm.Prefs.RecorderInsert == RecorderInsertPosition.Beginning);
        }
        catch (Exception ex) { _vm.Dialogs.Warn("VoxPad", ex.Message); }
    }
}
