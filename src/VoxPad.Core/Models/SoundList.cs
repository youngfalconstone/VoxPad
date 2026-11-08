namespace VoxPad.Core.Models;

/// <summary>
/// The whole document: sounds, the hidden "All sounds" order (which defines each sound's index), user categories,
/// hotbar tiles. Persisted by <see cref="Serialization.SoundListFile"/>.
/// </summary>
public sealed class SoundList
{
    public const int FormatVersion = 1;

    public int Version { get; set; } = FormatVersion;
    public List<SoundEntry> Sounds { get; set; } = new();
    /// <summary>Ordered ids; position + 1 is the sound's index (used by index hotkeys and the remote API).</summary>
    public List<Guid> AllSoundsOrder { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<HotbarTile> HotbarTiles { get; set; } = new();
    public int HotbarColumns { get; set; } = 8;
    public int HotbarRows { get; set; } = 2;
    public int HotbarPages { get; set; } = 4;
    public long TotalPlays { get; set; }

    public SoundEntry? Find(Guid id) => Sounds.Find(s => s.Id == id);

    public int IndexOf(Guid id) => AllSoundsOrder.IndexOf(id) + 1; // 1-based, 0 = not found

    public SoundEntry? ByIndex(int oneBased)
    {
        if (oneBased < 1 || oneBased > AllSoundsOrder.Count) return null;
        return Find(AllSoundsOrder[oneBased - 1]);
    }

    public IEnumerable<SoundEntry> SoundsInOrder()
    {
        foreach (var id in AllSoundsOrder)
        {
            var s = Find(id);
            if (s != null) yield return s;
        }
    }

    public void Add(SoundEntry s, Category? category = null, int? insertAt = null)
    {
        if (Find(s.Id) == null) Sounds.Add(s);
        if (!AllSoundsOrder.Contains(s.Id))
        {
            if (insertAt is int i && category == null && i >= 0 && i <= AllSoundsOrder.Count) AllSoundsOrder.Insert(i, s.Id);
            else AllSoundsOrder.Add(s.Id);
        }
        if (category != null && !category.SoundIds.Contains(s.Id))
        {
            if (insertAt is int j && j >= 0 && j <= category.SoundIds.Count) category.SoundIds.Insert(j, s.Id);
            else category.SoundIds.Add(s.Id);
        }
    }

    /// <summary>Removes a sound everywhere (All sounds, every category, hotbar).</summary>
    public void Remove(Guid id)
    {
        Sounds.RemoveAll(s => s.Id == id);
        AllSoundsOrder.Remove(id);
        foreach (var c in AllCategories()) c.SoundIds.Remove(id);
        HotbarTiles.RemoveAll(t => t.SoundId == id);
    }

    public IEnumerable<Category> AllCategories()
    {
        foreach (var c in Categories)
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }

    public Category? FindCategory(Guid id) => AllCategories().FirstOrDefault(c => c.Id == id);

    public Category? ParentOf(Category cat)
    {
        foreach (var c in AllCategories())
            if (c.Children.Contains(cat)) return c;
        return null;
    }

    /// <summary>Drops dangling ids after edits or imports.</summary>
    public void Repair()
    {
        var ids = Sounds.Select(s => s.Id).ToHashSet();
        AllSoundsOrder.RemoveAll(id => !ids.Contains(id));
        var inOrder = AllSoundsOrder.ToHashSet();
        foreach (var s in Sounds) if (!inOrder.Contains(s.Id)) AllSoundsOrder.Add(s.Id);
        foreach (var c in AllCategories()) c.SoundIds.RemoveAll(id => !ids.Contains(id));
        HotbarTiles.RemoveAll(t => !ids.Contains(t.SoundId));
    }

    public IEnumerable<(Hotkey hotkey, SoundEntry sound)> SoundHotkeys()
    {
        foreach (var s in Sounds) if (s.Hotkey != null) yield return (s.Hotkey, s);
    }

    public IEnumerable<(Hotkey hotkey, Category category)> CategoryHotkeys()
    {
        foreach (var c in AllCategories()) if (c.RandomHotkey != null) yield return (c.RandomHotkey, c);
    }
}
