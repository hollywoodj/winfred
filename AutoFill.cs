using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winfred;

/// <summary>
/// Opens a login page in the browser and fills it, the way Alfred's 1Password "open and
/// fill" does. Two strategies: press the 1Password extension's autofill shortcut (the
/// browser extension does the filling and never exposes the secret to us), or type the
/// credentials ourselves as a fallback.
///
/// Every synthetic keystroke is gated on the foreground window still belonging to a
/// browser, so a password can never be typed into whatever else grabbed focus.
/// </summary>
public static class AutoFill
{
    private static readonly string[] BrowserProcessNames =
    {
        "chrome", "msedge", "firefox", "brave", "vivaldi", "opera", "opera_gx", "chromium",
        "arc", "zen", "floorp", "librewolf", "waterfox", "thorium", "iexplore", "safari",
    };

    public sealed record FillRequest(
        string ItemTitle,
        string Url,
        string? Username,
        string? Password);

    /// <summary>Runs the whole open-then-fill sequence on a background thread.</summary>
    public static void OpenAndFill(FillRequest request)
    {
        var cfg = Config.Current.OnePassword;
        Task.Run(() =>
        {
            try
            {
                if (!LaunchBrowser(request.Url, cfg.BrowserPath))
                    return;

                if (cfg.FillMode.Equals("copyOnly", StringComparison.OrdinalIgnoreCase))
                {
                    Notifier.Notify($"Opened {request.ItemTitle} — filling is off (copy-only mode).");
                    return;
                }

                if (!WaitForBrowserForeground(cfg.PageLoadMs))
                {
                    Notifier.Notify($"Opened {request.ItemTitle}, but the browser never came to the front — not filling.");
                    return;
                }

                bool typed = cfg.FillMode.Equals("type", StringComparison.OrdinalIgnoreCase);
                if (typed)
                {
                    if (string.IsNullOrEmpty(request.Password))
                    {
                        Notifier.Notify($"“{request.ItemTitle}” has no password to type.");
                        return;
                    }
                    if (!TypeCredentials(request.Username, request.Password)) return;
                }
                else
                {
                    if (!PressExtensionShortcut(cfg.ExtensionShortcut)) return;
                    Thread.Sleep(Math.Clamp(cfg.SubmitDelayMs, 100, 10_000));
                }

                if (cfg.SubmitAfterFill)
                {
                    if (!typed) Thread.Sleep(200);
                    else Thread.Sleep(Math.Clamp(cfg.SubmitDelayMs, 100, 10_000));
                    if (IsBrowserForeground()) SendKey(0x0D); // Enter
                }

                Notifier.Notify(typed
                    ? $"Filled {request.ItemTitle}."
                    : $"Opened {request.ItemTitle} and triggered 1Password autofill.");
            }
            catch (Exception ex)
            {
                Notifier.Notify($"Open & fill failed: {ex.Message}");
            }
        });
    }

    /// <summary>Just opens the URL, no keystrokes.</summary>
    public static void OpenOnly(string url) => LaunchBrowser(url, Config.Current.OnePassword.BrowserPath);

    private static bool LaunchBrowser(string url, string browserPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(browserPath) && System.IO.File.Exists(browserPath))
                Process.Start(new ProcessStartInfo(browserPath) { Arguments = $"\"{url}\"", UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Notifier.Notify($"Couldn't open {url}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Polls until a browser owns the foreground window, then waits out the page load.</summary>
    private static bool WaitForBrowserForeground(int pageLoadMs)
    {
        int budget = Math.Clamp(pageLoadMs, 300, 30_000);
        var deadline = Environment.TickCount64 + Math.Max(budget, 4000);
        while (Environment.TickCount64 < deadline)
        {
            if (IsBrowserForeground())
            {
                Thread.Sleep(budget); // let the login form actually render
                return IsBrowserForeground();
            }
            Thread.Sleep(150);
        }
        return false;
    }

    public static bool IsBrowserForeground()
    {
        try
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == 0) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName.ToLowerInvariant();
            return BrowserProcessNames.Any(b => name == b || name.StartsWith(b));
        }
        catch
        {
            return false;
        }
    }

    private static bool PressExtensionShortcut(string shortcut)
    {
        if (!KeyCombo.TryParse(shortcut, out var combo))
        {
            Notifier.Notify($"“{shortcut}” isn't a valid autofill shortcut — check Settings → 1Password.");
            return false;
        }
        if (!IsBrowserForeground()) return false;
        SendCombo(combo);
        return true;
    }

    private static bool TypeCredentials(string? username, string? password)
    {
        if (!IsBrowserForeground()) return false;

        if (!string.IsNullOrEmpty(username))
        {
            TypeText(username);
            Thread.Sleep(60);
            SendKey(0x09); // Tab to the password field
            Thread.Sleep(120);
            if (!IsBrowserForeground()) return false; // focus moved — stop before the secret
        }

        TypeText(password!);
        return true;
    }

    // ---------- synthetic input ----------

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    public static void SendCombo(KeyCombo combo)
    {
        var down = new List<INPUT>();
        var up = new List<INPUT>();
        void Hold(ushort vk)
        {
            down.Add(KeyInput(vk, false));
            up.Insert(0, KeyInput(vk, true));
        }

        if (combo.Ctrl) Hold(0xA2);  // VK_LCONTROL
        if (combo.Alt) Hold(0xA4);   // VK_LMENU
        if (combo.Shift) Hold(0xA0); // VK_LSHIFT
        if (combo.Win) Hold(0x5B);   // VK_LWIN
        down.Add(KeyInput((ushort)combo.Vk, false));
        up.Insert(0, KeyInput((ushort)combo.Vk, true));

        Send(down.Concat(up).ToArray());
    }

    public static void SendKey(int vk) => Send(KeyInput((ushort)vk, false), KeyInput((ushort)vk, true));

    /// <summary>Types text as unicode scan codes, so layout and special characters don't matter.</summary>
    public static void TypeText(string text)
    {
        foreach (char c in text)
        {
            if (c == '\n' || c == '\r')
            {
                SendKey(0x0D);
                continue;
            }
            Send(UnicodeInput(c, false), UnicodeInput(c, true));
            Thread.Sleep(4); // a couple of ms per key keeps JS-driven fields in sync
        }
    }

    private static INPUT KeyInput(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    private static INPUT UnicodeInput(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
            },
        },
    };

    private static void Send(params INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

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
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
