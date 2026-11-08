using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VoxPad.App.Controls;
using VoxPad.App.Services;
using VoxPad.App.ViewModels;
using VoxPad.Audio.Editor;
using VoxPad.Audio.Engine;
using VoxPad.Core.Models;

namespace VoxPad.App.Views;

/// <summary>Right-click > Edit file: waveform, Cut (Del), Crop, volume, Undo/Redo, Zoom, selection by mouse/keyboard, play selection on speakers, attributes.</summary>
public sealed class EditorWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SoundEntry _entry;
    private WaveEditorDocument? _doc;
    private readonly WaveformControl _wave;
    private readonly TextBlock _selText, _status;
    private readonly Button _undo, _redo, _cut, _crop, _save;
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private int _playStartFrame;

    public EditorWindow(MainViewModel vm, SoundEntry entry)
    {
        _vm = vm; _entry = entry;
        Title = $"{Strings.Get("Editor", "Editor")} – {entry.Tag}"; Width = 900; Height = 520; MinWidth = 600; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource("ThemedWindow");
        var dock = new DockPanel { Margin = new Thickness(8) };

        // toolbar
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        Button B(string text, Action a, string? tip = null) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), ToolTip = tip }; b.Click += (_, _) => a(); bar.Children.Add(b); return b; }
        B("▶ " + Strings.Get("PlaySelection", "Play selection"), PlaySelection, "Space");
        B("⏹", () => _vm.Stop());
        _cut = B(Strings.Get("Cut", "Cut"), Cut, "Del");
        _crop = B(Strings.Get("Crop", "Crop"), Crop);
        B(Strings.Get("VolumeUp", "Vol +3 dB"), () => Volume(3));
        B(Strings.Get("VolumeDown", "Vol −3 dB"), () => Volume(-3));
        B(Strings.Get("VolumeCustom", "Volume…"), VolumeCustom);
        B(Strings.Get("NormalizeTo", "Normalize…"), NormalizeTo);
        _undo = B(Strings.Get("Undo", "Undo"), () => { _doc?.Undo(); Refresh(); }, "Ctrl+Z");
        _redo = B(Strings.Get("Redo", "Redo"), () => { _doc?.Redo(); Refresh(); }, "Ctrl+Y");
        B("🔍+", () => _wave.ZoomIn(), "Ctrl+wheel");
        B("🔍−", () => _wave.ZoomOut());
        B(Strings.Get("ZoomFit", "Fit"), () => _wave.ZoomFit());
        B(Strings.Get("ZoomSelection", "Zoom selection"), () => _wave.ZoomSelection());
        B(Strings.Get("SelectAll", "Select all"), () => _wave.SelectAll(), "Ctrl+A");
        _save = B("💾 " + Strings.Get("Save", "Save"), Save, "Ctrl+S");
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);

        // attributes
        var attrs = new TextBlock { Style = (Style)Application.Current.FindResource("Hint") };
        DockPanel.SetDock(attrs, Dock.Top); dock.Children.Add(attrs);

        // status
        _status = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(_status, Dock.Bottom); dock.Children.Add(_status);
        _selText = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontFamily = new FontFamily("Consolas") };
        DockPanel.SetDock(_selText, Dock.Bottom); dock.Children.Add(_selText);
        var hint = new TextBlock { Text = Strings.Get("EditorHint", "Drag to select · Shift+arrows / Home / End extend · Ctrl+A all · Ctrl+D deselect · Del cuts · Esc closes"), Style = (Style)Application.Current.FindResource("Hint") };
        DockPanel.SetDock(hint, Dock.Bottom); dock.Children.Add(hint);

        _wave = new WaveformControl
        {
            Background = (Brush)Application.Current.FindResource("Theme.PanelBackground"),
            WaveBrush = (Brush)Application.Current.FindResource("Theme.Accent"),
        };
        _wave.SelectionChanged += UpdateSelectionText;
        dock.Children.Add(new Border { BorderBrush = (Brush)Application.Current.FindResource("Theme.Border"), BorderThickness = new Thickness(1), Child = _wave });
        Content = dock;

        PreviewKeyDown += OnKey;
        Loaded += (_, _) =>
        {
            try
            {
                _doc = new WaveEditorDocument(entry.FilePath);
                _wave.Document = _doc;
                var a = _doc.Attributes;
                attrs.Text = $"{Strings.Get("Attr_Size", "Size")}: {a.FileSize / 1024:N0} KB   {Strings.Get("Attr_Format", "Format")}: {a.Format}   {Strings.Get("Attr_SampleRate", "Sample rate")}: {a.SampleRate} Hz   {Strings.Get("Attr_Duration", "Duration")}: {SoundItemViewModel.Format(a.Duration)}   {Strings.Get("Attr_Channels", "Channels")}: {a.Channels}   {Strings.Get("Attr_OriginalVolume", "Original volume")}: {(double.IsNaN(_doc.OriginalVolumeDb) ? "–" : _doc.OriginalVolumeDb.ToString("F1") + " dB")}";
                if (_doc.IsLong) _vm.Dialogs.Warn("VoxPad", Strings.Get("LongFileWarning", "This file is longer than 10 minutes. Editing keeps the whole file in memory and undo snapshots multiply that; expect high memory use."));
                Refresh();
                _wave.Focus();
            }
            catch (Exception ex)
            {
                _vm.Dialogs.Warn("VoxPad", Strings.Format("CannotOpenFile", ex.Message));
                Close();
            }
        };
        _cursorTimer.Tick += (_, _) =>
        {
            if (_vm.Engine.IsLoaded && _vm.Engine.Current?.Label == "editor")
                _wave.CursorFrame = _playStartFrame + (int)(_vm.Engine.Position.TotalSeconds * Audio.Decoding.EngineFormat.SampleRate);
            else if (_wave.CursorFrame >= 0) _wave.CursorFrame = -1;
        };
        _cursorTimer.Start();
        Closing += (_, e) =>
        {
            if (_doc is { IsDirty: true })
            {
                var r = MessageBox.Show(this, Strings.Get("EditorSaveQuestion", "Save the edited audio?"), "VoxPad", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (r == MessageBoxResult.Yes) Save();
            }
            _cursorTimer.Stop();
            if (_vm.Engine.Current?.Label == "editor") _vm.Stop();
        };
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Delete: Cut(); e.Handled = true; break;
            case Key.Space: PlaySelection(); e.Handled = true; break;
            case Key.Z when ctrl: _doc?.Undo(); Refresh(); e.Handled = true; break;
            case Key.Y when ctrl: _doc?.Redo(); Refresh(); e.Handled = true; break;
            case Key.S when ctrl: Save(); e.Handled = true; break;
        }
    }

    private void Refresh()
    {
        if (_doc == null) return;
        _wave.Document = _wave.Document; // re-clamp view
        _wave.InvalidateVisual();
        _undo.IsEnabled = _doc.CanUndo; _redo.IsEnabled = _doc.CanRedo;
        _save.IsEnabled = _doc.IsDirty;
        UpdateSelectionText();
        _status.Text = $"{Strings.Get("Attr_Duration", "Duration")}: {SoundItemViewModel.Format(_doc.Duration)}{(_doc.IsDirty ? "  •  " + Strings.Get("Modified", "modified") : "")}";
    }

    private void UpdateSelectionText()
    {
        if (_doc == null) return;
        double sr = Audio.Decoding.EngineFormat.SampleRate;
        _selText.Text = _wave.HasSelection
            ? $"{Strings.Get("Selection", "Selection")}: {_wave.SelectionStart / sr:F3}s – {_wave.SelectionEnd / sr:F3}s  ({(_wave.SelectionEnd - _wave.SelectionStart) / sr:F3}s)"
            : Strings.Get("NoSelection", "No selection (operations apply to the whole file)");
        _cut.IsEnabled = _crop.IsEnabled = _wave.HasSelection;
    }

    private void PlaySelection()
    {
        if (_doc == null) return;
        var (s, e) = _wave.HasSelection ? (_wave.SelectionStart, _wave.SelectionEnd) : (0, _doc.Frames);
        _playStartFrame = s;
        var pcm = _doc.Extract(s, e);
        _vm.Engine.PlayBuffer(pcm, "editor", PlayTarget.Speakers);
    }

    private void Cut() { if (_doc != null && _wave.HasSelection) { _doc.Cut(_wave.SelectionStart, _wave.SelectionEnd); _wave.Deselect(); Refresh(); } }
    private void Crop() { if (_doc != null && _wave.HasSelection) { _doc.Crop(_wave.SelectionStart, _wave.SelectionEnd); _wave.Deselect(); _wave.ZoomFit(); Refresh(); } }
    private void Volume(double db) { if (_doc != null) { _doc.AdjustVolume(_wave.SelectionStart, _wave.SelectionEnd, db); Refresh(); } }

    private void VolumeCustom()
    {
        var s = _vm.Dialogs.AskText(Strings.Get("VolumeCustom", "Volume"), Strings.Get("GainPrompt", "Gain in dB (negative = quieter):"), "0");
        if (double.TryParse(s, out var db)) Volume(db);
    }

    private void NormalizeTo()
    {
        var s = _vm.Dialogs.AskText(Strings.Get("NormalizeTo", "Normalize"), Strings.Get("NormalizePrompt", "Target in dB (89 ≈ −23 LUFS):"), _vm.Prefs.RecorderNormalizeTargetDb.ToString("F0"));
        if (double.TryParse(s, out var db) && _doc != null) { _doc.NormalizeTo(db); Refresh(); }
    }

    private void Save()
    {
        if (_doc == null) return;
        try
        {
            var path = _doc.Save(_vm.Prefs.RecorderFormat);
            if (!string.Equals(path, _entry.FilePath, StringComparison.OrdinalIgnoreCase)) _vm.ReplaceEntryFile(_entry, path);
            else _vm.ReplaceEntryFile(_entry, path);
            _vm.Status(Strings.Format("EditorSaved", path));
            Refresh();
        }
        catch (Exception ex) { _vm.Dialogs.Warn("VoxPad", ex.Message); }
    }
}
