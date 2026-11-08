using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VoxPad.Audio.Editor;

namespace VoxPad.App.Controls;

/// <summary>Waveform view with zoom, mouse selection and a playback cursor for the editor.</summary>
public sealed class WaveformControl : FrameworkElement
{
    private WaveEditorDocument? _doc;
    private int _viewStart, _viewEnd;    // frames
    private int _selStart = -1, _selEnd = -1;
    private int _cursor = -1;
    private bool _dragging;
    private int _dragAnchor;

    public Brush WaveBrush { get; set; } = Brushes.SteelBlue;
    public Brush SelectionBrush { get; set; } = new SolidColorBrush(Color.FromArgb(70, 88, 72, 200));
    public Brush Background { get; set; } = Brushes.White;
    public Brush CursorBrush { get; set; } = Brushes.OrangeRed;

    public event Action? SelectionChanged;

    public WaveformControl() { Focusable = true; Cursor = Cursors.IBeam; }

    public WaveEditorDocument? Document
    {
        get => _doc;
        set { _doc = value; _viewStart = 0; _viewEnd = value?.Frames ?? 0; _selStart = _selEnd = -1; InvalidateVisual(); }
    }

    public int SelectionStart => Math.Min(_selStart, _selEnd);
    public int SelectionEnd => Math.Max(_selStart, _selEnd);
    public bool HasSelection => _selStart >= 0 && _selEnd >= 0 && _selStart != _selEnd;
    public int CursorFrame { get => _cursor; set { _cursor = value; InvalidateVisual(); } }
    public int ViewStart => _viewStart;
    public int ViewEnd => _viewEnd;

    public void Select(int start, int end)
    {
        if (_doc == null) return;
        _selStart = Math.Clamp(start, 0, _doc.Frames); _selEnd = Math.Clamp(end, 0, _doc.Frames);
        InvalidateVisual(); SelectionChanged?.Invoke();
    }
    public void SelectAll() { if (_doc != null) Select(0, _doc.Frames); }
    public void Deselect() { _selStart = _selEnd = -1; InvalidateVisual(); SelectionChanged?.Invoke(); }

    public void ZoomIn() => Zoom(0.5);
    public void ZoomOut() => Zoom(2.0);
    public void ZoomFit() { if (_doc != null) { _viewStart = 0; _viewEnd = _doc.Frames; InvalidateVisual(); } }
    public void ZoomSelection() { if (HasSelection) { _viewStart = SelectionStart; _viewEnd = SelectionEnd; InvalidateVisual(); } }

    private void Zoom(double factor)
    {
        if (_doc == null) return;
        int center = HasSelection ? (SelectionStart + SelectionEnd) / 2 : (_viewStart + _viewEnd) / 2;
        int len = Math.Max(100, (int)((_viewEnd - _viewStart) * factor));
        len = Math.Min(len, _doc.Frames);
        _viewStart = Math.Clamp(center - len / 2, 0, Math.Max(0, _doc.Frames - len));
        _viewEnd = _viewStart + len;
        InvalidateVisual();
    }

    public void ScrollBy(double fraction)
    {
        if (_doc == null) return;
        int len = _viewEnd - _viewStart;
        int delta = (int)(len * fraction);
        _viewStart = Math.Clamp(_viewStart + delta, 0, Math.Max(0, _doc.Frames - len));
        _viewEnd = _viewStart + len;
        InvalidateVisual();
    }

    private int FrameAt(double x) => _doc == null || ActualWidth <= 0 ? 0 : _viewStart + (int)(Math.Clamp(x / ActualWidth, 0, 1) * (_viewEnd - _viewStart));
    private double XOf(int frame) => _viewEnd == _viewStart ? 0 : (frame - _viewStart) / (double)(_viewEnd - _viewStart) * ActualWidth;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        if (_doc == null || w < 2 || _viewEnd <= _viewStart) return;
        int cols = (int)w;
        var (min, max) = _doc.Peaks(cols, _viewStart, _viewEnd);
        double mid = h / 2;
        var pen = new Pen(WaveBrush, 1);
        for (int c = 0; c < cols; c++)
        {
            double y1 = mid - max[c] * mid * 0.95, y2 = mid - min[c] * mid * 0.95;
            if (y2 - y1 < 1) y2 = y1 + 1;
            dc.DrawLine(pen, new Point(c + 0.5, y1), new Point(c + 0.5, y2));
        }
        dc.DrawLine(new Pen(Brushes.Gray, 0.5), new Point(0, mid), new Point(w, mid));
        if (HasSelection)
        {
            double x1 = Math.Clamp(XOf(SelectionStart), 0, w), x2 = Math.Clamp(XOf(SelectionEnd), 0, w);
            dc.DrawRectangle(SelectionBrush, null, new Rect(x1, 0, Math.Max(1, x2 - x1), h));
        }
        if (_cursor >= _viewStart && _cursor <= _viewEnd)
        {
            double x = XOf(_cursor);
            dc.DrawLine(new Pen(CursorBrush, 1.5), new Point(x, 0), new Point(x, h));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_doc == null) return;
        _dragging = true; CaptureMouse();
        int f = FrameAt(e.GetPosition(this).X);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _selStart >= 0) { _selEnd = f; }
        else { _dragAnchor = f; _selStart = f; _selEnd = f; }
        InvalidateVisual(); SelectionChanged?.Invoke();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || _doc == null) return;
        _selEnd = FrameAt(e.GetPosition(this).X);
        InvalidateVisual(); SelectionChanged?.Invoke();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging) { _dragging = false; ReleaseMouseCapture(); SelectionChanged?.Invoke(); }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (e.Delta > 0) ZoomIn(); else ZoomOut(); }
        else ScrollBy(e.Delta > 0 ? -0.1 : 0.1);
        e.Handled = true;
    }

    /// <summary>Keyboard selection: Shift+arrows extend, Home/End jump, Ctrl+A all, Ctrl+D deselect.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_doc == null) return;
        int step = Math.Max(1, (_viewEnd - _viewStart) / 100);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.A when ctrl: SelectAll(); e.Handled = true; break;
            case Key.D when ctrl: Deselect(); e.Handled = true; break;
            case Key.Left: MoveEdge(-step, shift); e.Handled = true; break;
            case Key.Right: MoveEdge(step, shift); e.Handled = true; break;
            case Key.Home: if (shift) { _selEnd = 0; } else { _selStart = _selEnd = 0; } InvalidateVisual(); SelectionChanged?.Invoke(); e.Handled = true; break;
            case Key.End: if (shift) { _selEnd = _doc.Frames; } else { _selStart = _selEnd = _doc.Frames; } InvalidateVisual(); SelectionChanged?.Invoke(); e.Handled = true; break;
        }
    }

    private void MoveEdge(int delta, bool extend)
    {
        if (_doc == null) return;
        if (_selStart < 0) { _selStart = _selEnd = _viewStart; }
        if (extend) _selEnd = Math.Clamp(_selEnd + delta, 0, _doc.Frames);
        else { int p = Math.Clamp(SelectionStart + delta, 0, _doc.Frames); _selStart = _selEnd = p; }
        InvalidateVisual(); SelectionChanged?.Invoke();
    }
}
