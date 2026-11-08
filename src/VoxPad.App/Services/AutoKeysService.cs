using System.Diagnostics;
using System.Text;
using VoxPad.App.Interop;
using VoxPad.Core.Logging;
using VoxPad.Core.Models;
using VoxPad.Core.Settings;
using static VoxPad.App.Interop.NativeMethods;

namespace VoxPad.App.Services;

/// <summary>Result of evaluating one Auto Key entry; shown by the Troubleshooting dialog.</summary>
public sealed record AutoKeyEvaluation(AutoKeyEntry Entry, bool Fired, string Reason);

/// <summary>
/// Automatic push-to-talk: when a sound starts on the microphone, the configured key is pressed (and held for the
/// sound's duration, or tapped before and after) so the game or voice app opens its PTT gate.
/// </summary>
public sealed class AutoKeysService
{
    private readonly Preferences _prefs;
    private readonly List<AutoKeyEntry> _held = new();
    private readonly List<AutoKeyEntry> _tapAfter = new();
    private readonly object _gate = new();

    public bool Enabled { get => _prefs.AutoKeysEnabled; set => _prefs.AutoKeysEnabled = value; }
    public IReadOnlyList<AutoKeyEvaluation> LastEvaluation { get; private set; } = Array.Empty<AutoKeyEvaluation>();
    public DateTime? LastEvaluationTime { get; private set; }
    public string LastGlobalReason { get; private set; } = "";

    public AutoKeysService(Preferences prefs) => _prefs = prefs;

    /// <summary>Called when a sound starts. Evaluates every entry and presses keys as configured.</summary>
    public void OnSoundStarted(bool onMic, bool playedByHotkey)
    {
        ReleaseAll();
        var evals = new List<AutoKeyEvaluation>();
        LastEvaluationTime = DateTime.Now;
        if (!Enabled) { LastGlobalReason = "Auto Keys are disabled (status bar indicator)."; LastEvaluation = evals; return; }
        if (!onMic) { LastGlobalReason = "The sound was not played on the microphone (play mode is Speakers only, or the microphone is not available)."; LastEvaluation = evals; return; }
        LastGlobalReason = "";

        IntPtr fg = GetForegroundWindow();
        string fgProcess = ProcessNameOf(fg);
        bool fgFullscreen = IsFullscreen(fg, out var fgDetail);

        foreach (var e in _prefs.AutoKeys)
        {
            if (!e.Enabled) { evals.Add(new(e, false, "Entry is disabled.")); continue; }
            if (e.Key == null) { evals.Add(new(e, false, "No key assigned.")); continue; }
            if (e.OnlyIfPlayedByHotkey && !playedByHotkey) { evals.Add(new(e, false, "Sound was not started by a hotkey and 'only if played by hotkey' is on.")); continue; }
            string reason;
            bool ok = e.Condition switch
            {
                AutoKeyCondition.Always => Pass(out reason, "Always."),
                AutoKeyCondition.ForegroundFullscreen => fgFullscreen ? Pass(out reason, "Foreground app is fullscreen: " + fgDetail) : Fail(out reason, "Foreground window is not fullscreen: " + fgDetail),
                AutoKeyCondition.ListedAppRunning => AnyRunning(e.Apps, out var which) ? Pass(out reason, $"'{which}' is running.") : Fail(out reason, "None of the listed apps is running: " + string.Join(", ", e.Apps)),
                AutoKeyCondition.ListedAppForeground => e.Apps.Any(a => Matches(a, fgProcess)) ? Pass(out reason, $"'{fgProcess}' is in the foreground.") : Fail(out reason, $"Foreground app is '{fgProcess}', not one of: " + string.Join(", ", e.Apps)),
                _ => Fail(out reason, "Unknown condition"),
            };
            if (!ok) { evals.Add(new(e, false, reason)); continue; }
            try
            {
                lock (_gate)
                {
                    if (e.PressMode == AutoKeyPressMode.PressAndHold) { Send(e.Key, down: true); _held.Add(e); }
                    else { Send(e.Key, true); Thread.Sleep(30); Send(e.Key, false); _tapAfter.Add(e); }
                }
                evals.Add(new(e, true, reason));
            }
            catch (Exception ex) { evals.Add(new(e, false, "SendInput failed: " + ex.Message)); }
        }
        LastEvaluation = evals;
    }

    /// <summary>Called when the sound stops or finishes: releases held keys / sends the trailing tap.</summary>
    public void OnSoundStopped()
    {
        lock (_gate)
        {
            foreach (var e in _tapAfter)
            {
                try { if (e.Key != null) { Send(e.Key, true); Thread.Sleep(30); Send(e.Key, false); } } catch (Exception ex) { Log.Warn("auto key tap: " + ex.Message); }
            }
            _tapAfter.Clear();
        }
        ReleaseAll();
    }

    public void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (var e in _held)
            {
                try { if (e.Key != null) Send(e.Key, false); } catch (Exception ex) { Log.Warn("auto key release: " + ex.Message); }
            }
            _held.Clear();
        }
    }

    private static bool Pass(out string r, string s) { r = s; return true; }
    private static bool Fail(out string r, string s) { r = s; return false; }

    private static bool Matches(string listed, string process)
    {
        var a = Path.GetFileNameWithoutExtension(listed);
        return a.Equals(process, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AnyRunning(IEnumerable<string> apps, out string which)
    {
        which = "";
        foreach (var a in apps)
        {
            var name = Path.GetFileNameWithoutExtension(a);
            try { if (Process.GetProcessesByName(name).Length > 0) { which = name; return true; } } catch { }
        }
        return false;
    }

    public static string ProcessNameOf(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    /// <summary>Fullscreen incl. borderless: the window rect covers its monitor and it is not the desktop/shell.</summary>
    public static bool IsFullscreen(IntPtr hwnd, out string detail)
    {
        detail = "no foreground window";
        if (hwnd == IntPtr.Zero) return false;
        var cls = new StringBuilder(64); GetClassName(hwnd, cls, 64);
        var title = new StringBuilder(128); GetWindowText(hwnd, title, 128);
        string c = cls.ToString();
        if (c is "Progman" or "WorkerW" or "Shell_TrayWnd") { detail = $"desktop/shell ({c})"; return false; }
        if (!GetWindowRect(hwnd, out var r)) return false;
        var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        bool full = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        detail = $"'{title}' [{c}] {r.Width}x{r.Height} on monitor {mi.rcMonitor.Width}x{mi.rcMonitor.Height}";
        return full;
    }

    /// <summary>Presses or releases a hotkey's modifiers + main key using scan codes (what games expect).</summary>
    public static void Send(Hotkey key, bool down)
    {
        var inputs = new List<INPUT>();
        var mods = key.Modifiers;
        void Key(int vk)
        {
            uint scan = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
            bool extended = vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5 or 0x6F or 0x90;
            uint flags = KEYEVENTF_SCANCODE | (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = (ushort)scan, dwFlags = flags } } });
        }
        var modVks = new List<int>();
        if (mods.HasFlag(HotkeyModifiers.Ctrl)) modVks.Add(mods.HasFlag(HotkeyModifiers.RightCtrl) ? VK_RCONTROL : VK_LCONTROL);
        if (mods.HasFlag(HotkeyModifiers.Alt)) modVks.Add(mods.HasFlag(HotkeyModifiers.RightAlt) ? VK_RMENU : VK_LMENU);
        if (mods.HasFlag(HotkeyModifiers.Shift)) modVks.Add(mods.HasFlag(HotkeyModifiers.RightShift) ? VK_RSHIFT : VK_LSHIFT);
        if (mods.HasFlag(HotkeyModifiers.Win)) modVks.Add(mods.HasFlag(HotkeyModifiers.RightWin) ? VK_RWIN : VK_LWIN);

        if (down) foreach (var m in modVks) Key(m);
        if (key.IsMouse)
        {
            uint f = key.Input switch
            {
                HotkeyInput.MouseLeft => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
                HotkeyInput.MouseRight => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
                HotkeyInput.MouseMiddle => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
                _ => down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
            };
            uint data = key.Input switch { HotkeyInput.MouseX1 => 1u, HotkeyInput.MouseX2 => 2u, _ => 0u };
            inputs.Add(new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = f, mouseData = data } } });
        }
        else Key(key.VirtualKey);
        if (!down) for (int i = modVks.Count - 1; i >= 0; i--) Key(modVks[i]);
        var arr = inputs.ToArray();
        var sent = SendInput((uint)arr.Length, arr, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        if (sent != arr.Length) throw new InvalidOperationException("SendInput error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
}
