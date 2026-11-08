using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using VoxPad.Core.Models;

namespace VoxPad.Core.Serialization;

/// <summary>
/// VoxPad list format (.vpl): UTF-8 JSON. Stores references, categories, order, hotkeys, colours — never audio.
/// Also imports Soundpad's XML sound lists (.spl) on a best-effort basis.
/// </summary>
public static class SoundListFile
{
    public const string Extension = ".vpl";
    public const string SoundpadExtension = ".spl";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new HotkeyJsonConverter() },
    };

    public static string Serialize(SoundList list) => JsonSerializer.Serialize(ToDto(list), Options);

    public static SoundList Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<ListDto>(json, Options) ?? throw new InvalidDataException("Empty sound list");
        var list = FromDto(dto);
        list.Repair();
        return list;
    }

    public static void Save(SoundList list, string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(list));
        File.Move(tmp, path, overwrite: true);
    }

    public static SoundList Load(string path)
    {
        if (string.Equals(Path.GetExtension(path), SoundpadExtension, StringComparison.OrdinalIgnoreCase))
            return ImportSoundpad(path);
        var list = Deserialize(File.ReadAllText(path));
        ResolveRelativePaths(list, Path.GetDirectoryName(path));
        return list;
    }

    /// <summary>Exported lists use paths relative to the list file where possible, so a folder can be moved to a second PC.</summary>
    public static void SaveRelative(SoundList list, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        var dto = ToDto(list);
        foreach (var s in dto.Sounds)
        {
            if (Path.IsPathRooted(s.Path) && s.Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                s.Path = Path.GetRelativePath(dir, s.Path);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(dto, Options));
    }

    private static void ResolveRelativePaths(SoundList list, string? baseDir)
    {
        if (baseDir == null) return;
        foreach (var s in list.Sounds)
            if (!Path.IsPathRooted(s.FilePath)) s.FilePath = Path.GetFullPath(Path.Combine(baseDir, s.FilePath));
    }

    // ---- DTOs keep the file format decoupled from the runtime model ----

    private sealed class ListDto
    {
        public int Version { get; set; } = SoundList.FormatVersion;
        public string App { get; set; } = "VoxPad";
        public List<SoundDto> Sounds { get; set; } = new();
        public List<Guid> Order { get; set; } = new();
        public List<CategoryDto> Categories { get; set; } = new();
        public HotbarDto Hotbar { get; set; } = new();
        public long TotalPlays { get; set; }
    }

    private sealed class SoundDto
    {
        public Guid Id { get; set; }
        public string Path { get; set; } = "";
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public long DurationMs { get; set; }
        public Hotkey? Hotkey { get; set; }
        public int PlayCount { get; set; }
        public DateTime? LastPlayed { get; set; }
        public uint Color { get; set; }
        public DateTime Added { get; set; }
    }

    private sealed class CategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public List<Guid> Sounds { get; set; } = new();
        public List<CategoryDto> Children { get; set; } = new();
        public Hotkey? RandomHotkey { get; set; }
        public bool Hidden { get; set; }
        public uint Color { get; set; }
        public bool Expanded { get; set; } = true;
    }

    private sealed class HotbarDto
    {
        public int Columns { get; set; } = 8;
        public int Rows { get; set; } = 2;
        public int Pages { get; set; } = 4;
        public List<HotbarTile> Tiles { get; set; } = new();
    }

    private static ListDto ToDto(SoundList l) => new()
    {
        Version = SoundList.FormatVersion,
        Sounds = l.Sounds.Select(s => new SoundDto
        {
            Id = s.Id, Path = s.FilePath, Title = s.Title, Artist = s.Artist, DurationMs = s.DurationMs, Hotkey = s.Hotkey,
            PlayCount = s.PlayCount, LastPlayed = s.LastPlayed, Color = s.Color, Added = s.Added,
        }).ToList(),
        Order = l.AllSoundsOrder.ToList(),
        Categories = l.Categories.Select(ToDto).ToList(),
        Hotbar = new HotbarDto { Columns = l.HotbarColumns, Rows = l.HotbarRows, Pages = l.HotbarPages, Tiles = l.HotbarTiles.ToList() },
        TotalPlays = l.TotalPlays,
    };

    private static CategoryDto ToDto(Category c) => new()
    {
        Id = c.Id, Name = c.Name, Sounds = c.SoundIds.ToList(), Children = c.Children.Select(ToDto).ToList(),
        RandomHotkey = c.RandomHotkey, Hidden = c.Hidden, Color = c.Color, Expanded = c.Expanded,
    };

    private static SoundList FromDto(ListDto d) => new()
    {
        Version = d.Version,
        Sounds = d.Sounds.Select(s => new SoundEntry
        {
            Id = s.Id, FilePath = s.Path, Title = s.Title, Artist = s.Artist, DurationMs = s.DurationMs, Hotkey = s.Hotkey,
            PlayCount = s.PlayCount, LastPlayed = s.LastPlayed, Color = s.Color, Added = s.Added,
        }).ToList(),
        AllSoundsOrder = d.Order.ToList(),
        Categories = d.Categories.Select(FromDto).ToList(),
        HotbarColumns = d.Hotbar.Columns, HotbarRows = d.Hotbar.Rows, HotbarPages = d.Hotbar.Pages,
        HotbarTiles = d.Hotbar.Tiles.ToList(),
        TotalPlays = d.TotalPlays,
    };

    private static Category FromDto(CategoryDto c) => new()
    {
        Id = c.Id, Name = c.Name, SoundIds = c.Sounds.ToList(), Children = c.Children.Select(FromDto).ToList(),
        RandomHotkey = c.RandomHotkey, Hidden = c.Hidden, Color = c.Color, Expanded = c.Expanded,
    };

    // ---- Soundpad .spl import (XML) ----

    /// <summary>
    /// Best-effort import of a Soundpad list. Soundpad stores <c>&lt;Soundlist&gt;&lt;Sound url=".." artist=".." title=".."
    /// duration=".." playCount=".." lastPlayedOn=".." hotkey=".." color=".."/&gt;</c> plus <c>&lt;Category name=".."&gt;</c> trees
    /// that reference sounds by index. Attribute names vary by Soundpad version, so lookups are case-insensitive and lenient.
    /// </summary>
    public static SoundList ImportSoundpad(string path)
    {
        var doc = XDocument.Load(path);
        var list = new SoundList();
        var root = doc.Root ?? throw new InvalidDataException("Not a Soundpad list");
        var byIndex = new Dictionary<int, SoundEntry>();

        static string? Attr(XElement e, params string[] names)
        {
            foreach (var n in names)
            {
                var a = e.Attributes().FirstOrDefault(x => string.Equals(x.Name.LocalName, n, StringComparison.OrdinalIgnoreCase));
                if (a != null) return a.Value;
            }
            return null;
        }

        int i = 0;
        foreach (var e in root.Descendants().Where(x => x.Name.LocalName.Equals("Sound", StringComparison.OrdinalIgnoreCase)))
        {
            var url = Attr(e, "url", "path", "file");
            if (string.IsNullOrWhiteSpace(url)) continue;
            i++;
            var s = new SoundEntry
            {
                FilePath = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url,
                Title = Attr(e, "title"),
                Artist = Attr(e, "artist"),
            };
            if (long.TryParse(Attr(e, "duration"), out var ms)) s.DurationMs = ms;
            if (int.TryParse(Attr(e, "playCount"), out var pc)) s.PlayCount = pc;
            if (DateTime.TryParse(Attr(e, "lastPlayedOn", "lastPlayed"), out var lp)) s.LastPlayed = lp;
            if (DateTime.TryParse(Attr(e, "addedDate", "added"), out var ad)) s.Added = ad;
            var color = Attr(e, "color");
            if (!string.IsNullOrWhiteSpace(color))
            {
                if (color.StartsWith("#")) color = color[1..];
                if (uint.TryParse(color, System.Globalization.NumberStyles.HexNumber, null, out var argb))
                    s.Color = color.Length <= 6 ? 0xFF000000 | argb : argb;
            }
            var hk = Attr(e, "hotkey");
            if (!string.IsNullOrWhiteSpace(hk)) s.Hotkey = ParseSoundpadHotkey(hk);
            var idx = int.TryParse(Attr(e, "index"), out var ix) ? ix : i;
            byIndex[idx] = s;
            list.Add(s);
        }

        foreach (var ce in root.Elements().Where(x => x.Name.LocalName.Equals("Categories", StringComparison.OrdinalIgnoreCase)).SelectMany(x => x.Elements()))
            list.Categories.Add(ImportCategory(ce, byIndex));
        list.Repair();
        return list;

        Category ImportCategory(XElement e, Dictionary<int, SoundEntry> map)
        {
            var c = new Category { Name = Attr(e, "name") ?? "Category" };
            foreach (var child in e.Elements())
            {
                if (child.Name.LocalName.Equals("Category", StringComparison.OrdinalIgnoreCase)) c.Children.Add(ImportCategory(child, map));
                else if (child.Name.LocalName.Equals("Sound", StringComparison.OrdinalIgnoreCase) && int.TryParse(Attr(child, "index"), out var ix) && map.TryGetValue(ix, out var s))
                    c.SoundIds.Add(s.Id);
            }
            var hk = Attr(e, "hotkey", "randomHotkey");
            if (!string.IsNullOrWhiteSpace(hk)) c.RandomHotkey = ParseSoundpadHotkey(hk);
            return c;
        }
    }

    /// <summary>Parses "Ctrl+Alt+F5", "Num 3", "Mouse 4" style strings.</summary>
    public static Hotkey? ParseSoundpadHotkey(string text)
    {
        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "windows": mods |= HotkeyModifiers.Win; break;
                default: key = part; break;
            }
        }
        if (key == null) return null;
        var lower = key.ToLowerInvariant();
        if (lower.StartsWith("mouse"))
        {
            var b = lower.Replace("mouse", "").Trim();
            return b switch
            {
                "mid" or "middle" or "3" => Hotkey.Mouse(HotkeyInput.MouseMiddle, mods),
                "4" or "x1" => Hotkey.Mouse(HotkeyInput.MouseX1, mods),
                "5" or "x2" => Hotkey.Mouse(HotkeyInput.MouseX2, mods),
                _ => null,
            };
        }
        if (key.Length == 1) return Hotkey.Key(char.ToUpperInvariant(key[0]), mods);
        if (lower.StartsWith("f") && int.TryParse(lower[1..], out var fn) && fn is >= 1 and <= 24) return Hotkey.Key(0x70 + fn - 1, mods);
        if (lower.StartsWith("num ") && int.TryParse(lower[4..], out var nd)) return Hotkey.Key(0x60 + nd, mods);
        var found = Enumerable.Range(0, 256).FirstOrDefault(vk => VirtualKeyNames.Name(vk).Equals(key, StringComparison.OrdinalIgnoreCase), -1);
        return found >= 0 ? Hotkey.Key(found, mods) : null;
    }
}

public sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
{
    public override Hotkey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? Hotkey.Deserialize(reader.GetString()) : null;

    public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Serialize());
}
