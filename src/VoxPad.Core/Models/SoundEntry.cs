namespace VoxPad.Core.Models;

/// <summary>A reference to one audio file inside the sound list. The audio itself is never stored in the list.</summary>
public sealed class SoundEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = "";
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public long DurationMs { get; set; }
    public Hotkey? Hotkey { get; set; }
    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }
    /// <summary>ARGB colour, 0 = none.</summary>
    public uint Color { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;

    /// <summary>Runtime only: file could not be found at the last check.</summary>
    public bool IsMissing { get; set; }

    /// <summary>Soundpad "Tag": "Artist – Title" from metadata, else the file name without extension.</summary>
    public string Tag
    {
        get
        {
            var hasTitle = !string.IsNullOrWhiteSpace(Title);
            var hasArtist = !string.IsNullOrWhiteSpace(Artist);
            if (hasTitle && hasArtist) return $"{Artist} – {Title}";
            if (hasTitle) return Title!;
            if (hasArtist) return Artist!;
            return Path.GetFileNameWithoutExtension(FilePath);
        }
    }

    public SoundEntry Clone() => (SoundEntry)MemberwiseClone();
}
