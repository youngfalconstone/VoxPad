using System.Collections.ObjectModel;
using System.Windows.Media;
using VoxPad.Core.Models;

namespace VoxPad.App.ViewModels;

public sealed class HotbarTileViewModel : ObservableObject
{
    private bool _isPlaying;
    public int Page { get; }
    public int Slot { get; }
    public HotbarTile? Tile { get; private set; }
    public SoundEntry? Sound { get; private set; }

    public HotbarTileViewModel(int page, int slot) { Page = page; Slot = slot; }

    public void Bind(HotbarTile? tile, SoundEntry? sound) { Tile = tile; Sound = sound; RaiseAll(); }

    public bool IsEmpty => Sound == null;
    public string Label => Sound?.Tag ?? "";
    public string HotkeyText => Sound?.Hotkey?.ToString() ?? "";
    public bool IsMissing => Sound?.IsMissing ?? false;
    public uint EffectiveColor => Tile?.Color is > 0 and var c ? c : Sound?.Color ?? 0;
    public Brush? ColorBrush => EffectiveColor == 0 ? null : SoundItemViewModel.BrushFor(EffectiveColor);
    public bool HasColor => EffectiveColor != 0;
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }
}

public sealed class HotbarViewModel : ObservableObject
{
    private int _currentPage;
    private SoundList _list = new();

    public ObservableCollection<HotbarTileViewModel> Tiles { get; } = new();
    public int Columns => _list.HotbarColumns;
    public int Rows => _list.HotbarRows;
    public int Pages => _list.HotbarPages;
    public string PageText => $"{_currentPage + 1} / {Pages}";

    public int CurrentPage
    {
        get => _currentPage;
        set { if (Set(ref _currentPage, Math.Clamp(value, 0, Math.Max(0, Pages - 1)))) { Rebuild(); Raise(nameof(PageText)); } }
    }

    public void Attach(SoundList list)
    {
        _list = list;
        if (_currentPage >= Pages) _currentPage = 0;
        Rebuild();
        RaiseAll();
    }

    public void Rebuild()
    {
        Tiles.Clear();
        for (int slot = 0; slot < Columns * Rows; slot++)
        {
            var vm = new HotbarTileViewModel(_currentPage, slot);
            var tile = _list.HotbarTiles.FirstOrDefault(t => t.Page == _currentPage && t.Slot == slot);
            vm.Bind(tile, tile == null ? null : _list.Find(tile.SoundId));
            Tiles.Add(vm);
        }
    }

    public void NextPage() => CurrentPage = (_currentPage + 1) % Math.Max(1, Pages);
    public void PreviousPage() => CurrentPage = (_currentPage - 1 + Math.Max(1, Pages)) % Math.Max(1, Pages);

    public void MarkPlaying(Guid? soundId)
    {
        foreach (var t in Tiles) t.IsPlaying = soundId != null && t.Sound?.Id == soundId;
    }
}
