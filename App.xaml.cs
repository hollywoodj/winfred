using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Winfred;

/// <summary>Adds/removes Winfred from the per-user Run key.</summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue("Winfred") != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (value) key.SetValue("Winfred", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Winfred", false);
        }
    }
}

public partial class App : System.Windows.Application
{
    private const string MutexName = "WinfredSingleInstance";
    private const string ShowSignalName = "WinfredShowSignal";

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private KeyboardHook? _hook;
    private WinForms.NotifyIcon? _tray;
    private MainWindow? _window;
    private SettingsWindow? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        if (!isFirstInstance)
        {
            _showSignal.Set(); // ask the running instance to show itself, then bail
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            Notifier.Notify($"Something went wrong: {args.Exception.Message}");
            args.Handled = true; // a failed action shouldn't take the tray app down with it
        };

        Config.Load();
        Usage.Load();
        Theme.Apply(Config.Current.Appearance.Theme);

        _window = new MainWindow();
        new WindowInteropHelper(_window).EnsureHandle(); // create the HWND early so the first summon is instant

        SearchEngine.OpenSettings = ShowSettings;
        _window.Engine.Start();

        InstallHook();
        SetupTray();
        Notifier.Handler = msg => Dispatcher.BeginInvoke(() =>
            _tray?.ShowBalloonTip(3000, "Winfred", msg, WinForms.ToolTipIcon.None));

        // Anything that changes the config — settings window, tray reload, an edited file —
        // funnels through here so every component picks the change up at once.
        Config.Changed += OnConfigChanged;

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(() => _window?.ShowLauncher()), null, -1, false);

        if (e.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(ShowSettings);
    }

    /// <summary>Appends to %APPDATA%\Winfred\error.log so a tray-only app can still be debugged.</summary>
    private static void LogError(Exception exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Config.Directory);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Config.Directory, "error.log"),
                $"{DateTime.Now:s} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // if we can't even log, there's nothing useful left to do
        }
    }

    private void OnConfigChanged() => Dispatcher.BeginInvoke(() =>
    {
        InstallHook();
        _window?.ApplyAppearance();
        _window?.Engine.Reconfigure();
        if (_tray != null) _tray.Text = $"Winfred — {Config.Current.Hotkey.Describe()}";
    });

    public void ShowLauncher() => _window?.ShowLauncher();

    public void ShowSettings()
    {
        _window?.HideLauncher();
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_window!.Engine);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    private void InstallHook()
    {
        _hook?.Dispose();
        try
        {
            _hook = new KeyboardHook(Config.Current.Hotkey, Dispatcher);
            _hook.Triggered += () => _window?.Toggle();
            _hook.Install();
        }
        catch (Exception ex)
        {
            Notifier.Notify($"Couldn't register the summon hotkey: {ex.Message}");
        }
    }

    private void SetupTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show Winfred", null, (_, _) => _window?.ShowLauncher());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Rebuild file index", null, (_, _) =>
        {
            _window?.Engine.Files.Rebuild();
            Notifier.Notify("Rebuilding the file index…");
        });
        menu.Items.Add("Edit config file", null, (_, _) => OpenConfig());
        menu.Items.Add("Reload config", null, (_, _) =>
        {
            Config.Load();
            Notifier.Notify("Config reloaded.");
        });
        var autostart = new WinForms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled,
        };
        autostart.CheckedChanged += (_, _) => Autostart.IsEnabled = autostart.Checked;
        menu.Items.Add(autostart);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        _tray = new WinForms.NotifyIcon
        {
            Icon = Branding.TrayIcon(),
            Visible = true,
            Text = $"Winfred — {Config.Current.Hotkey.Describe()}",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => _window?.ShowLauncher();
        _tray.ShowBalloonTip(2500, "Winfred",
            $"Running. {Capitalize(Config.Current.Hotkey.Describe())} to open.", WinForms.ToolTipIcon.None);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static void OpenConfig() =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Config.FilePath}\"") { UseShellExecute = true });

    protected override void OnExit(ExitEventArgs e)
    {
        Config.Changed -= OnConfigChanged;
        _showWait?.Unregister(null);
        _hook?.Dispose();
        _window?.Engine.Dispose();
        Usage.SaveNow();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _mutex?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }
}
