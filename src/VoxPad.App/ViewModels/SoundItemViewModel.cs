using System.Windows.Media;
using VoxPad.Core.Models;

namespace VoxPad.App.ViewModels;

public sealed class SoundItemViewModel : ObservableObject
{
    private bool _isPlaying;
    private int _index;

    public SoundEntry Entry { get; }
    public SoundItemViewModel(SoundEntry entry, int index) { Entry = entry; _index = index; }

    public Guid Id => Entry.Id;
    public int Index { get => _index; set => Set(ref _index, value); }
    public string Tag => Entry.Tag;
    public string FilePath => Entry.FilePath;
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Entry.DurationMs);
    public string DurationText => Format(Duration);
    public string HotkeyText => Entry.Hotkey?.ToString() ?? "";
    public int PlayCount => Entry.PlayCount;
    public DateTime? LastPlayed => Entry.LastPlayed;
    public string LastPlayedText => Entry.LastPlayed?.ToString("g") ?? "";
    public bool IsMissing => Entry.IsMissing;
    public bool HasColor => Entry.Color != 0;
    public Brush? ColorBrush => Entry.Color == 0 ? null : BrushFor(Entry.Color);
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }

    public void Refresh() => RaiseAll();

    public static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public static SolidColorBrush BrushFor(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
}
