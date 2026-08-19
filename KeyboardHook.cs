using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Winfred;

/// <summary>
/// The global summon hotkey, in two flavours.
///
/// <b>Double-tap</b> fires when a modifier is tapped twice in quick succession with no other
/// key involved. For Win and Alt a bare tap has an OS side effect (Start menu / menu-bar
/// focus), so those taps are masked: the real key-up is swallowed and replaced with
/// [dummy-key down, dummy-key up, modifier up], which Windows treats as a combo. A genuine
/// single tap is re-sent after the double-tap window expires, so a lone Win tap still opens
/// the Start menu — just slightly delayed.
///
/// <b>Combo</b> fires on an exact chord such as Ctrl+Space, and swallows it so the
/// foreground app never sees it.
///
/// Either flavour stands down while a full-screen app is in front (see <see cref="FullscreenGuard"/>)
/// so a stray tap mid-game costs neither the keystroke nor a second of muted audio.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    public event Action? Triggered;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const ushort VK_MASK = 0xE8; // unassigned virtual-key code, safe to inject
    private const uint KEYEVENTF_KEYUP = 0x2;

    private readonly bool _comboMode;
    private readonly KeyCombo? _combo;
    private readonly int[] _targetVks = Array.Empty<int>();
    private readonly bool _needsMasking;
    private readonly bool _suppressInFullscreen;
    private readonly int _windowMs;
    private readonly Dispatcher _dispatcher;

    private nint _hookId;
    private HookProc? _proc; // field keeps the delegate alive for the native hook

    private bool _targetDown;
    private bool _contaminated;   // another key was pressed while the target was held
    private bool _pendingDouble;  // this press started within the double-tap window
    private long _lastTapAt;
    private bool _comboSwallowed;
    private DispatcherTimer? _forwardTimer;
    private ushort _forwardVk;

    public KeyboardHook(HotkeyConfig hotkey, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _windowMs = Math.Clamp(hotkey.DoubleTapMs, 150, 1000);
        _suppressInFullscreen = hotkey.SuppressInFullscreen;

        if (hotkey.Mode.Equals("combo", StringComparison.OrdinalIgnoreCase) &&
            KeyCombo.TryParse(hotkey.Combo, out var combo) && combo.HasModifier)
        {
            _comboMode = true;
            _combo = combo;
            return;
        }

        (_targetVks, _needsMasking) = hotkey.Key.Trim().ToLowerInvariant() switch
        {
            "win" or "cmd" or "command" => (new[] { 0x5B, 0x5C }, true), // VK_LWIN / VK_RWIN
            "alt" => (new[] { 0xA4, 0xA5 }, true),                       // VK_LMENU / VK_RMENU
            "shift" => (new[] { 0xA0, 0xA1 }, false),                    // VK_LSHIFT / VK_RSHIFT
            _ => (new[] { 0xA2, 0xA3 }, false),                          // ctrl (default fallback)
        };
    }

    public void Install()
    {
        _proc = Callback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hookId == 0)
            throw new InvalidOperationException(
                $"Failed to install keyboard hook (win32 error {Marshal.GetLastWin32Error()}).");
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((info.flags & LLKHF_INJECTED) != 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam); // synthetic input, incl. our own

        int msg = (int)wParam;
        bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;

        if (_comboMode)
            return ComboCallback(info, isDown, isUp, nCode, wParam, lParam);

        bool isTarget = Array.IndexOf(_targetVks, (int)info.vkCode) >= 0;
        long now = Environment.TickCount64;

        if (isDown)
        {
            if (isTarget)
            {
                if (!_targetDown) // ignore key auto-repeat
                {
                    _targetDown = true;
                    _contaminated = false;
                    _pendingDouble = _lastTapAt != 0 && now - _lastTapAt <= _windowMs;
                    if (_pendingDouble)
                        StopForwardTimer(); // second tap arrived — don't replay the first one
                }
            }
            else
            {
                if (_targetDown) _contaminated = true; // it's a combo like Win+L, leave it alone
                _lastTapAt = 0;
                _pendingDouble = false;
                StopForwardTimer();
            }
        }
        else if (isUp && isTarget && _targetDown)
        {
            _targetDown = false;

            if (_contaminated || Suppressed())
            {
                _lastTapAt = 0;
                _pendingDouble = false;
                StopForwardTimer();
            }
            else if (_pendingDouble)
            {
                _pendingDouble = false;
                _lastTapAt = 0;
                Fire();
                if (_needsMasking)
                {
                    MaskBareUp((ushort)info.vkCode);
                    return 1; // swallow the real key-up
                }
            }
            else
            {
                _lastTapAt = now;
                if (_needsMasking)
                {
                    MaskBareUp((ushort)info.vkCode);
                    StartForwardTimer((ushort)info.vkCode);
                    return 1; // swallow the real key-up
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private nint ComboCallback(KBDLLHOOKSTRUCT info, bool isDown, bool isUp, int nCode, nint wParam, nint lParam)
    {
        if ((int)info.vkCode == _combo!.Vk)
        {
            if (isDown && ModifiersMatch())
            {
                if (Suppressed())
                    return CallNextHookEx(_hookId, nCode, wParam, lParam); // let the game have the chord
                _comboSwallowed = true;
                Fire();
                return 1; // the foreground app never sees the chord
            }
            if (isUp && _comboSwallowed)
            {
                _comboSwallowed = false;
                return 1; // swallow the matching key-up too
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>Exact match: every required modifier held, every other modifier released.</summary>
    private bool ModifiersMatch()
    {
        bool ctrl = IsDown(0x11), alt = IsDown(0x12), shift = IsDown(0x10);
        bool win = IsDown(0x5B) || IsDown(0x5C);
        return ctrl == _combo!.Ctrl && alt == _combo.Alt && shift == _combo.Shift && win == _combo.Win;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private void Fire() => _dispatcher.BeginInvoke(() => Triggered?.Invoke());

    private bool Suppressed() => _suppressInFullscreen && FullscreenGuard.IsFullscreenAppForeground();

    private static void MaskBareUp(ushort vk) =>
        SendKeys(Key(VK_MASK, up: false), Key(VK_MASK, up: true), Key(vk, up: true));

    private void StartForwardTimer(ushort vk)
    {
        _forwardVk = vk;
        _forwardTimer ??= CreateForwardTimer();
        _forwardTimer.Stop();
        _forwardTimer.Interval = TimeSpan.FromMilliseconds(_windowMs + 30);
        _forwardTimer.Start();
    }

    private DispatcherTimer CreateForwardTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, _dispatcher);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _lastTapAt = 0;
            // No second tap came: replay a clean single tap so Start menu / Alt menu still work
            SendKeys(Key(_forwardVk, up: false), Key(_forwardVk, up: true));
        };
        return timer;
    }

    private void StopForwardTimer() => _forwardTimer?.Stop();

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = 1, // INPUT_KEYBOARD
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    private static void SendKeys(params INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    public void Dispose()
    {
        StopForwardTimer();
        if (_hookId != 0)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = 0;
        }
        _proc = null;
    }

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi; // present so the union has the size Windows expects
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
