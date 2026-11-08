using System.Text;

namespace VoxPad.Core.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1, Alt = 2, Shift = 4, Win = 8,
    // Side-specific bits are only honoured when "Allow left/right modifiers" is on.
    LeftCtrl = 16, RightCtrl = 32, LeftAlt = 64, RightAlt = 128,
    LeftShift = 256, RightShift = 512, LeftWin = 1024, RightWin = 2048,
    SideMask = LeftCtrl | RightCtrl | LeftAlt | RightAlt | LeftShift | RightShift | LeftWin | RightWin,
}

/// <summary>Input source of the main key of a hotkey.</summary>
public enum HotkeyInput { Keyboard = 0, MouseMiddle = 1, MouseX1 = 2, MouseX2 = 3, MouseLeft = 4, MouseRight = 5 }

/// <summary>
/// A global hotkey: an optional modifier set plus one main key (keyboard virtual-key code or mouse button).
/// Bare left / right mouse buttons are rejected by <see cref="IsAllowed"/>; modifier+click is fine.
/// </summary>
public sealed record Hotkey(HotkeyInput Input, int VirtualKey, HotkeyModifiers Modifiers)
{
    public static Hotkey Key(int vk, HotkeyModifiers mods = HotkeyModifiers.None) => new(HotkeyInput.Keyboard, vk, mods);
    public static Hotkey Mouse(HotkeyInput button, HotkeyModifiers mods = HotkeyModifiers.None) => new(button, 0, mods);

    public bool IsMouse => Input != HotkeyInput.Keyboard;

    /// <summary>Soundpad rule: left/right click without a modifier would make the app unusable.</summary>
    public bool IsAllowed => !((Input == HotkeyInput.MouseLeft || Input == HotkeyInput.MouseRight) && (Modifiers & ~HotkeyModifiers.SideMask) == HotkeyModifiers.None);

    /// <summary>Normalised form used for matching: side bits folded into generic bits unless sides are honoured.</summary>
    public Hotkey Normalize(bool keepSides)
    {
        var m = Modifiers;
        if ((m & (HotkeyModifiers.LeftCtrl | HotkeyModifiers.RightCtrl)) != 0) m |= HotkeyModifiers.Ctrl;
        if ((m & (HotkeyModifiers.LeftAlt | HotkeyModifiers.RightAlt)) != 0) m |= HotkeyModifiers.Alt;
        if ((m & (HotkeyModifiers.LeftShift | HotkeyModifiers.RightShift)) != 0) m |= HotkeyModifiers.Shift;
        if ((m & (HotkeyModifiers.LeftWin | HotkeyModifiers.RightWin)) != 0) m |= HotkeyModifiers.Win;
        if (!keepSides) m &= ~HotkeyModifiers.SideMask;
        return this with { Modifiers = m };
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        void Add(string s) { if (sb.Length > 0) sb.Append('+'); sb.Append(s); }
        var m = Modifiers;
        if (m.HasFlag(HotkeyModifiers.LeftCtrl)) Add("LCtrl"); else if (m.HasFlag(HotkeyModifiers.RightCtrl)) Add("RCtrl"); else if (m.HasFlag(HotkeyModifiers.Ctrl)) Add("Ctrl");
        if (m.HasFlag(HotkeyModifiers.LeftAlt)) Add("LAlt"); else if (m.HasFlag(HotkeyModifiers.RightAlt)) Add("RAlt"); else if (m.HasFlag(HotkeyModifiers.Alt)) Add("Alt");
        if (m.HasFlag(HotkeyModifiers.LeftShift)) Add("LShift"); else if (m.HasFlag(HotkeyModifiers.RightShift)) Add("RShift"); else if (m.HasFlag(HotkeyModifiers.Shift)) Add("Shift");
        if (m.HasFlag(HotkeyModifiers.LeftWin)) Add("LWin"); else if (m.HasFlag(HotkeyModifiers.RightWin)) Add("RWin"); else if (m.HasFlag(HotkeyModifiers.Win)) Add("Win");
        Add(Input switch
        {
            HotkeyInput.MouseMiddle => "Mouse Mid",
            HotkeyInput.MouseX1 => "Mouse 4",
            HotkeyInput.MouseX2 => "Mouse 5",
            HotkeyInput.MouseLeft => "Mouse Left",
            HotkeyInput.MouseRight => "Mouse Right",
            _ => VirtualKeyNames.Name(VirtualKey),
        });
        return sb.ToString();
    }

    public string Serialize() => $"{(int)Input}:{VirtualKey}:{(int)Modifiers}";

    public static Hotkey? Deserialize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var p = s.Split(':');
        if (p.Length != 3) return null;
        if (!int.TryParse(p[0], out var i) || !int.TryParse(p[1], out var vk) || !int.TryParse(p[2], out var m)) return null;
        return new Hotkey((HotkeyInput)i, vk, (HotkeyModifiers)m);
    }
}

/// <summary>Human readable names for Win32 virtual-key codes (subset covering every key on a standard keyboard).</summary>
public static class VirtualKeyNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "Caps Lock", [0x1B] = "Esc",
        [0x20] = "Space", [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "Print Screen", [0x2D] = "Insert", [0x2E] = "Delete",
        [0x5B] = "LWin", [0x5C] = "RWin", [0x5D] = "Apps",
        [0x60] = "Num 0", [0x61] = "Num 1", [0x62] = "Num 2", [0x63] = "Num 3", [0x64] = "Num 4", [0x65] = "Num 5", [0x66] = "Num 6", [0x67] = "Num 7", [0x68] = "Num 8", [0x69] = "Num 9",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6C] = "Num Sep", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        [0x90] = "Num Lock", [0x91] = "Scroll Lock",
        [0xA0] = "LShift", [0xA1] = "RShift", [0xA2] = "LCtrl", [0xA3] = "RCtrl", [0xA4] = "LAlt", [0xA5] = "RAlt",
        [0xA6] = "Browser Back", [0xA7] = "Browser Forward", [0xAD] = "Volume Mute", [0xAE] = "Volume Down", [0xAF] = "Volume Up",
        [0xB0] = "Media Next", [0xB1] = "Media Prev", [0xB2] = "Media Stop", [0xB3] = "Media Play",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0xE2] = "\\ (102)",
    };

    public static string Name(int vk)
    {
        if (Names.TryGetValue(vk, out var n)) return n;
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
        return "VK" + vk.ToString("X2");
    }

    public static bool IsModifierKey(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);
    public static bool IsNumpadDigit(int vk) => vk >= 0x60 && vk <= 0x69;
    public static bool IsTopRowDigit(int vk) => vk >= 0x30 && vk <= 0x39;
    public static int DigitValue(int vk) => IsNumpadDigit(vk) ? vk - 0x60 : IsTopRowDigit(vk) ? vk - 0x30 : -1;
}
