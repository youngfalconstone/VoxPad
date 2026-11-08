namespace VoxPad.Core.Models;

public enum IconSize { Small = 0, Medium = 1, Large = 2, ExtraLarge = 3 }

/// <summary>A (nestable) category. Holds an ordered list of sound ids; a sound may be in several categories.</summary>
public sealed class Category
{
    public const string AllSoundsName = "All sounds";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Guid> SoundIds { get; set; } = new();
    public List<Category> Children { get; set; } = new();
    public Hotkey? RandomHotkey { get; set; }
    public bool Hidden { get; set; }
    public uint Color { get; set; }
    public bool Expanded { get; set; } = true;

    public IEnumerable<Category> Descendants()
    {
        foreach (var c in Children)
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }
}
