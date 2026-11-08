using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoxPad.App.Controls;

/// <summary>Horizontal peak meter (0..1) with green/yellow/red zones; used for the mic level in the toolbar and in the recorder.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    private static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0xB0, 0x5A)));
    private static readonly Brush Yellow = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x30)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x34, 0x2B)));
    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRoundedRectangle(Background, null, new Rect(0, 0, w, h), 2, 2);
        // dBFS-ish scaling so speech is visible: sqrt compresses the low end.
        double v = Math.Clamp(Math.Sqrt(Math.Clamp(Level, 0, 1)), 0, 1);
        double x = v * w;
        if (x <= 0) return;
        double g = Math.Min(x, w * 0.7), y = Math.Min(x, w * 0.9);
        dc.DrawRectangle(Green, null, new Rect(0, 1, g, h - 2));
        if (x > w * 0.7) dc.DrawRectangle(Yellow, null, new Rect(w * 0.7, 1, y - w * 0.7, h - 2));
        if (x > w * 0.9) dc.DrawRectangle(Red, null, new Rect(w * 0.9, 1, x - w * 0.9, h - 2));
    }
}
