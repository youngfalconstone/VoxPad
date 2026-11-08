using System.Runtime.InteropServices;
using System.Windows.Threading;
using VoxPad.App.Interop;
using VoxPad.Core.Logging;
using VoxPad.Core.Models;
using static VoxPad.App.Interop.NativeMethods;

namespace VoxPad.App.Services;

public enum HotkeyActionKind { Sound, CategoryRandom, Special, Numpad, Index }

/// <summary>What a matched hotkey means. Numpad/Index carry the typed number.</summary>
public sealed record HotkeyAction(HotkeyActionKind Kind, Guid Id = default, string Special = "", int Number = 0);

/// <summary>
/// Global hotkeys via low-level keyboard + mouse hooks on a dedicated thread. Works when the app is not focused.
/// Matching happens inside the hook so the key can be swallowed (Pass hotkeys off) before the foreground app sees it.
/// Injected input (Auto Keys' own SendInput) is ignored so the app never triggers itself.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _kbHook, _mouseHook;
    private HookProc? _kbProc, _mouseProc; // keep delegates alive
    private volatile Dictionary<Hotkey, HotkeyAction> _bindings = new();
    private readonly HashSet<int> _swallowedKeys = new();
    private readonly object _gate = new();
    private Action<Hotkey>? _capture;
    private bool _captureModifierDown;
    private int _captureModifierVk;

    // Numpad / index gesture state
    private int _gestureDigits = -1;        // -1 = no gesture in progress
    private bool _gestureIsIndex;
    private HotkeyModifiers _gestureModifier;

    public bool Enabled { get; set; } = true;
    public bool PassHotkeys { get; set; }
    public bool AllowLeftRightModifiers { get; set; }
    public bool NumpadEnabled { get; set; } = true;
    public HotkeyModifiers NumpadModifier { get; set; } = HotkeyModifiers.Alt;
    public bool IndexEnabled { get; set; } = true;
    public HotkeyModifiers IndexModifier { get; set; } = HotkeyModifiers.Alt;
    /// <summary>Special hotkeys that must keep working while hotkeys are disabled.</summary>
    public HashSet<string> AlwaysActiveSpecials { get; } = new() { "EnableHotkeys", "ToggleHotkeys" };

    public event Action<HotkeyAction>? Triggered;
    public bool IsInstalled => _kbHook != IntPtr.Zero;
    public string? InstallError { get; private set; }

    public HotkeyService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "VoxPad.Hooks", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void SetBindings(IEnumerable<(Hotkey hotkey, HotkeyAction action)> bindings)
    {
        var d = new Dictionary<Hotkey, HotkeyAction>();
        foreach (var (hk, a) in bindings)
        {
            if (hk == null) continue;
            var n = hk.Normalize(AllowLeftRightModifiers);
            d[n] = a; // last wins; the view model keeps assignments unique
        }
        _bindings = d;
    }

    /// <summary>Capture mode: the next complete key/mouse combination is reported instead of being matched.</summary>
    public void BeginCapture(Action<Hotkey> onCaptured) { lock (_gate) { _capture = onCaptured; _captureModifierDown = false; } }
    public void EndCapture() { lock (_gate) _capture = null; }

    private void HookThread()
    {
        _threadId = GetCurrentThreadId();
        _kbProc = KeyboardProc; _mouseProc = MouseProc;
        var hMod = GetModuleHandle(null);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_kbHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            InstallError = "SetWindowsHookEx failed: " + Marshal.GetLastWin32Error();
            Log.Error(InstallError);
        }
        else Log.Info("Global hooks installed");
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _kbHook = _mouseHook = IntPtr.Zero;
    }

    private static HotkeyModifiers CurrentModifiers(int excludeVk)
    {
        var m = HotkeyModifiers.None;
        bool Down(int vk) => vk != excludeVk && IsKeyDown(vk);
        if (Down(VK_LCONTROL)) m |= HotkeyModifiers.Ctrl | HotkeyModifiers.LeftCtrl;
        if (Down(VK_RCONTROL)) m |= HotkeyModifiers.Ctrl | HotkeyModifiers.RightCtrl;
        if (Down(VK_LMENU)) m |= HotkeyModifiers.Alt | HotkeyModifiers.LeftAlt;
        if (Down(VK_RMENU)) m |= HotkeyModifiers.Alt | HotkeyModifiers.RightAlt;
        if (Down(VK_LSHIFT)) m |= HotkeyModifiers.Shift | HotkeyModifiers.LeftShift;
        if (Down(VK_RSHIFT)) m |= HotkeyModifiers.Shift | HotkeyModifiers.RightShift;
        if (Down(VK_LWIN)) m |= HotkeyModifiers.Win | HotkeyModifiers.LeftWin;
        if (Down(VK_RWIN)) m |= HotkeyModifiers.Win | HotkeyModifiers.RightWin;
        return m;
    }

    private static bool ModifierHeld(HotkeyModifiers wanted, HotkeyModifiers current)
    {
        var generic = wanted & ~HotkeyModifiers.SideMask;
        return generic != HotkeyModifiers.None && (current & ~HotkeyModifiers.SideMask) == generic;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((info.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        int msg = (int)wParam;
        bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool up = msg is WM_KEYUP or WM_SYSKEYUP;
        int vk = (int)info.vkCode;
        bool swallow = false;
        try
        {
            if (down) swallow = OnKeyDown(vk);
            else if (up) swallow = OnKeyUp(vk);
        }
        catch (Exception ex) { Log.Error("hook", ex); }
        return swallow ? (IntPtr)1 : CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private bool OnKeyDown(int vk)
    {
        bool isMod = VirtualKeyNames.IsModifierKey(vk);
        var mods = CurrentModifiers(vk);

        // ---- capture mode ----
        Action<Hotkey>? cap;
        lock (_gate) cap = _capture;
        if (cap != null)
        {
            if (isMod) { _captureModifierDown = true; _captureModifierVk = vk; return true; }
            var hk = Hotkey.Key(vk, mods);
            lock (_gate) _capture = null;
            _dispatcher.BeginInvoke(() => cap(hk));
            return true;
        }

        // ---- numpad / index gestures ----
        if (!isMod && _gestureDigits >= 0 && !PassHotkeysNow())
        {
            int digit = VirtualKeyNames.DigitValue(vk);
            bool matchesSet = _gestureIsIndex ? VirtualKeyNames.IsTopRowDigit(vk) : VirtualKeyNames.IsNumpadDigit(vk);
            if (digit >= 0 && matchesSet && ModifierHeld(_gestureModifier, mods))
            {
                _gestureDigits = Math.Min(_gestureDigits * 10 + digit, 99999);
                return true;
            }
        }
        if (!isMod && Enabled && _gestureDigits < 0)
        {
            int digit = VirtualKeyNames.DigitValue(vk);
            if (digit >= 0)
            {
                if (NumpadEnabled && VirtualKeyNames.IsNumpadDigit(vk) && ModifierHeld(NumpadModifier, mods))
                { _gestureDigits = digit; _gestureIsIndex = false; _gestureModifier = NumpadModifier; return !PassHotkeys; }
                if (IndexEnabled && VirtualKeyNames.IsTopRowDigit(vk) && ModifierHeld(IndexModifier, mods))
                { _gestureDigits = digit; _gestureIsIndex = true; _gestureModifier = IndexModifier; return !PassHotkeys; }
            }
        }

        // ---- plain bindings ----
        lock (_gate) if (_swallowedKeys.Contains(vk)) return !PassHotkeys; // auto-repeat of a matched key
        var key = Hotkey.Key(vk, mods).Normalize(AllowLeftRightModifiers);
        if (_bindings.TryGetValue(key, out var action) && IsActionAllowed(action))
        {
            lock (_gate) _swallowedKeys.Add(vk);
            Fire(action);
            return !PassHotkeys;
        }
        return false;
    }

    private bool OnKeyUp(int vk)
    {
        bool isMod = VirtualKeyNames.IsModifierKey(vk);
        Action<Hotkey>? cap;
        lock (_gate) cap = _capture;
        if (cap != null && isMod && _captureModifierDown && vk == _captureModifierVk)
        {
            // A bare modifier was pressed and released: it becomes the hotkey itself.
            var hk = Hotkey.Key(vk, CurrentModifiers(vk));
            lock (_gate) _capture = null;
            _dispatcher.BeginInvoke(() => cap(hk));
            return true;
        }

        if (_gestureDigits >= 0 && isMod)
        {
            var remaining = CurrentModifiers(vk);
            if (!ModifierHeld(_gestureModifier, remaining))
            {
                int number = _gestureDigits; bool index = _gestureIsIndex;
                _gestureDigits = -1;
                if (number > 0) Fire(new HotkeyAction(index ? HotkeyActionKind.Index : HotkeyActionKind.Numpad, Number: number));
            }
        }

        bool swallowed;
        lock (_gate) swallowed = _swallowedKeys.Remove(vk);
        return swallowed && !PassHotkeys;
    }

    private bool PassHotkeysNow() => PassHotkeys;

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if ((info.flags & LLMHF_INJECTED) != 0) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        int msg = (int)wParam;
        HotkeyInput? button = msg switch
        {
            WM_MBUTTONDOWN or WM_MBUTTONUP => HotkeyInput.MouseMiddle,
            WM_XBUTTONDOWN or WM_XBUTTONUP => (info.mouseData >> 16) == 1 ? HotkeyInput.MouseX1 : HotkeyInput.MouseX2,
            WM_LBUTTONDOWN or WM_LBUTTONUP => HotkeyInput.MouseLeft,
            WM_RBUTTONDOWN or WM_RBUTTONUP => HotkeyInput.MouseRight,
            _ => null,
        };
        if (button == null) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        bool down = msg is WM_MBUTTONDOWN or WM_XBUTTONDOWN or WM_LBUTTONDOWN or WM_RBUTTONDOWN;
        bool swallow = false;
        try
        {
            var mods = CurrentModifiers(-1);
            var hk = Hotkey.Mouse(button.Value, mods);
            int pseudoVk = 0x1000 + (int)button.Value;
            if (down)
            {
                Action<Hotkey>? cap;
                lock (_gate) cap = _capture;
                if (cap != null)
                {
                    if (!hk.IsAllowed) return CallNextHookEx(_mouseHook, nCode, wParam, lParam); // bare left/right: let it through, dialog explains
                    lock (_gate) _capture = null;
                    _dispatcher.BeginInvoke(() => cap(hk));
                    return (IntPtr)1;
                }
                if (hk.IsAllowed && _bindings.TryGetValue(hk.Normalize(AllowLeftRightModifiers), out var action) && IsActionAllowed(action))
                {
                    lock (_gate) _swallowedKeys.Add(pseudoVk);
                    Fire(action);
                    swallow = !PassHotkeys;
                }
            }
            else
            {
                bool was; lock (_gate) was = _swallowedKeys.Remove(pseudoVk);
                swallow = was && !PassHotkeys;
            }
        }
        catch (Exception ex) { Log.Error("mouse hook", ex); }
        return swallow ? (IntPtr)1 : CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsActionAllowed(HotkeyAction a) => Enabled || (a.Kind == HotkeyActionKind.Special && AlwaysActiveSpecials.Contains(a.Special));

    private void Fire(HotkeyAction action) => _dispatcher.BeginInvoke(() => Triggered?.Invoke(action));

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
    }
}
