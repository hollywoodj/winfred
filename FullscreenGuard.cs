using System.Runtime.InteropServices;
using System.Text;

namespace Winfred;

/// <summary>
/// Detects whether a full-screen app — in practice a game or a video player — owns the foreground.
///
/// Summoning the launcher over one of those is a bad trade: <see cref="MainWindow.ForceForeground"/>
/// takes focus away, the game reacts by muting its audio, and if it is running exclusive full-screen
/// the launcher never gets composited on top anyway. The game grabs the foreground straight back,
/// Winfred's Deactivated handler hides it again, and the whole round trip costs a second of silence
/// and shows nothing. So the hotkey stands down while such a window is in front.
///
/// The probe is cheap (four user32 calls) but runs from the low-level keyboard hook, so results are
/// cached briefly to keep that callback off the hot path during normal typing.
/// </summary>
public static class FullscreenGuard
{
    private const int CacheMs = 500;

    /// <summary>Desktop and shell windows fill a monitor without being full-screen apps.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "MultitaskingViewFrame",
    };

    private static long _checkedAt;
    private static bool _cached;

    public static bool IsFullscreenAppForeground()
    {
        long now = Environment.TickCount64;
        if (_checkedAt != 0 && now - _checkedAt < CacheMs)
            return _cached;
        _checkedAt = now;
        _cached = Probe();
        return _cached;
    }

    private static bool Probe()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == GetCurrentProcessId()) return false; // the launcher or the settings window

        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        if (ShellClasses.Contains(cls.ToString())) return false;

        // A maximised window still leaves the taskbar reachable, so it isn't the problem case.
        if (IsIconic(hwnd) || IsZoomed(hwnd)) return false;

        if (!GetWindowRect(hwnd, out RECT window)) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info)) return false;

        var screen = info.rcMonitor;
        return window.Left <= screen.Left && window.Top <= screen.Top &&
               window.Right >= screen.Right && window.Bottom >= screen.Bottom;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
}
