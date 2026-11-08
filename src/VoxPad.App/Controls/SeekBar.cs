using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VoxPad.App.Controls;

/// <summary>
/// Seek slider whose fill is red while others can hear the sound (microphone) and blue when it only plays on the speakers.
/// Value is a fraction 0..1; dragging/clicking raises SeekRequested with the new fraction.
/// </summary>
public sealed class SeekBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SeekBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsMicProperty = DependencyProperty.Register(nameof(IsMic), typeof(bool), typeof(SeekBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MicBrushProperty = DependencyProperty.Register(nameof(MicBrush), typeof(Brush), typeof(SeekBar),
        new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SpeakerBrushProperty = DependencyProperty.Register(nameof(SpeakerBrush), typeof(Brush), typeof(SeekBar),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(SeekBar),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsMic { get => (bool)GetValue(IsMicProperty); set => SetValue(IsMicProperty, value); }
    public Brush MicBrush { get => (Brush)GetValue(MicBrushProperty); set => SetValue(MicBrushProperty, value); }
    public Brush SpeakerBrush { get => (Brush)GetValue(SpeakerBrushProperty); set => SetValue(SpeakerBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    public event Action<double>? SeekRequested;
    private bool _dragging;

    public SeekBar() { MinHeight = 14; Cursor = Cursors.Hand; }

    /// <summary>Colour currently used for the fill: the acceptance test "seek bar colour matches output target" reads this.</summary>
    public Brush CurrentFillBrush => IsMic ? MicBrush : SpeakerBrush;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double trackH = 6, y = (h - trackH) / 2;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, y, w, trackH), 3, 3);
        double x = Math.Clamp(Value, 0, 1) * w;
        if (x > 0) dc.DrawRoundedRectangle(CurrentFillBrush, null, new Rect(0, y, x, trackH), 3, 3);
        dc.DrawEllipse(CurrentFillBrush, null, new Point(x, h / 2), 5, 5);
    }

    private void Seek(Point p)
    {
        if (ActualWidth <= 0) return;
        var f = Math.Clamp(p.X / ActualWidth, 0, 1);
        Value = f;
        SeekRequested?.Invoke(f);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true; CaptureMouse(); Seek(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) Seek(e.GetPosition(this));
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging) { _dragging = false; ReleaseMouseCapture(); Seek(e.GetPosition(this)); }
    }
}
