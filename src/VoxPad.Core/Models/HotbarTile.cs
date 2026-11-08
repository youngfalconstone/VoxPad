namespace VoxPad.Core.Models;

/// <summary>One tile on the paged hotbar grid. Hotkeys live on the sound, so they are shared across pages.</summary>
public sealed class HotbarTile
{
    public int Page { get; set; }
    public int Slot { get; set; }
    public Guid SoundId { get; set; }
    /// <summary>Tile colour override; 0 = use the sound's colour.</summary>
    public uint Color { get; set; }
}
