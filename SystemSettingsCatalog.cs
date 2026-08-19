using System.Diagnostics;

namespace Winfred;

/// <summary>
/// A searchable catalog of Windows settings pages and classic control panels, so
/// "set bluetooth" or "set display" jumps straight to the right page.
/// </summary>
public static class SystemSettingsCatalog
{
    public sealed record SettingsPage(string Name, string Target, string Keywords, string Group)
    {
        /// <summary>True for ms-settings:/shell: URIs; false for executables like devmgmt.msc.</summary>
        public bool IsUri => Target.Contains(':') && !Target.Contains('\\');
    }

    public static void Open(SettingsPage page)
    {
        try
        {
            // A few entries carry arguments ("control.exe /name …"); ShellExecute needs them split out.
            string target = page.Target;
            string arguments = "";
            if (!page.IsUri)
            {
                int space = target.IndexOf(' ');
                if (space > 0)
                {
                    arguments = target[(space + 1)..];
                    target = target[..space];
                }
            }
            Process.Start(new ProcessStartInfo(target) { Arguments = arguments, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifier.Notify($"Couldn't open {page.Name}: {ex.Message}");
        }
    }

    public static List<SettingsPage> Search(string query, int limit)
    {
        string trimmed = query.Trim();
        if (trimmed.Length == 0)
            return Pages.Take(limit).ToList();

        var terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Pages
            .Select(page => (page, score: Score(page, terms)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => x.page)
            .ToList();
    }

    private static double Score(SettingsPage page, string[] terms)
    {
        double total = 0;
        foreach (var term in terms)
        {
            double score = FuzzyMatcher.Score(page.Name, term);
            if (score <= 0 && page.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase)) score = 30;
            if (score <= 0 && page.Group.Contains(term, StringComparison.OrdinalIgnoreCase)) score = 12;
            if (score <= 0) return 0;
            total += score;
        }
        return total / terms.Length;
    }

    public static readonly List<SettingsPage> Pages = new()
    {
        // ---- System ----
        new("Display", "ms-settings:display", "screen resolution scaling monitor brightness hdr", "System"),
        new("Night light", "ms-settings:nightlight", "blue light warm colour temperature", "System"),
        new("Graphics settings", "ms-settings:display-advancedgraphics", "gpu performance games", "System"),
        new("Advanced display", "ms-settings:display-advanced", "refresh rate hz monitor", "System"),
        new("Sound", "ms-settings:sound", "audio volume speakers microphone output input", "System"),
        new("Volume mixer", "ms-settings:apps-volume", "per app volume audio", "System"),
        new("Notifications", "ms-settings:notifications", "alerts banners do not disturb toast", "System"),
        new("Focus assist", "ms-settings:quiethours", "focus do not disturb quiet hours", "System"),
        new("Power & battery", "ms-settings:powersleep", "sleep screen timeout battery saver", "System"),
        new("Battery saver", "ms-settings:batterysaver", "power battery", "System"),
        new("Storage", "ms-settings:storagesense", "disk space cleanup temporary files", "System"),
        new("Storage Sense", "ms-settings:storagepolicies", "cleanup automatic disk", "System"),
        new("Nearby sharing", "ms-settings:crossdevice", "share phone link", "System"),
        new("Multitasking", "ms-settings:multitasking", "snap windows alt tab desktops", "System"),
        new("Activate Windows", "ms-settings:activation", "licence license product key", "System"),
        new("Troubleshoot", "ms-settings:troubleshoot", "fix problems diagnostics", "System"),
        new("Recovery", "ms-settings:recovery", "reset this pc restore advanced startup", "System"),
        new("Projecting to this PC", "ms-settings:project", "wireless display miracast", "System"),
        new("Remote Desktop", "ms-settings:remotedesktop", "rdp remote access", "System"),
        new("Clipboard", "ms-settings:clipboard", "clipboard history sync paste", "System"),
        new("About your PC", "ms-settings:about", "device specs windows version rename pc", "System"),
        new("Optional features", "ms-settings:optionalfeatures", "install features rsat", "System"),

        // ---- Bluetooth & devices ----
        new("Bluetooth & devices", "ms-settings:bluetooth", "pair bluetooth wireless headphones", "Devices"),
        new("Printers & scanners", "ms-settings:printers", "printer scanner add printer", "Devices"),
        new("Mouse", "ms-settings:mousetouchpad", "pointer speed scroll cursor", "Devices"),
        new("Touchpad", "ms-settings:devices-touchpad", "trackpad gestures", "Devices"),
        new("Typing", "ms-settings:typing", "autocorrect keyboard suggestions", "Devices"),
        new("Pen & Windows Ink", "ms-settings:pen", "stylus handwriting", "Devices"),
        new("AutoPlay", "ms-settings:autoplay", "removable drive usb default action", "Devices"),
        new("USB", "ms-settings:usb", "usb notifications", "Devices"),
        new("Camera", "ms-settings:camera", "webcam", "Devices"),
        new("Mobile devices", "ms-settings:mobile-devices", "phone link android", "Devices"),

        // ---- Network ----
        new("Network & internet", "ms-settings:network", "internet connection", "Network"),
        new("Wi-Fi", "ms-settings:network-wifi", "wireless wifi networks", "Network"),
        new("Known Wi-Fi networks", "ms-settings:network-wifisettings", "saved wifi forget network", "Network"),
        new("Ethernet", "ms-settings:network-ethernet", "wired lan", "Network"),
        new("VPN", "ms-settings:network-vpn", "vpn connection", "Network"),
        new("Mobile hotspot", "ms-settings:network-mobilehotspot", "tethering share connection", "Network"),
        new("Airplane mode", "ms-settings:network-airplanemode", "flight mode radios", "Network"),
        new("Proxy", "ms-settings:network-proxy", "proxy server pac", "Network"),
        new("Dial-up", "ms-settings:network-dialup", "modem", "Network"),
        new("Advanced network settings", "ms-settings:network-advancedsettings", "adapters reset network", "Network"),
        new("Network reset", "ms-settings:network-status", "reset adapters status data usage", "Network"),

        // ---- Personalisation ----
        new("Personalisation", "ms-settings:personalization", "themes appearance", "Personalisation"),
        new("Background", "ms-settings:personalization-background", "wallpaper desktop image slideshow", "Personalisation"),
        new("Colours", "ms-settings:personalization-colors", "accent colour dark mode light mode transparency", "Personalisation"),
        new("Themes", "ms-settings:themes", "theme desktop icons", "Personalisation"),
        new("Lock screen", "ms-settings:lockscreen", "screensaver lock background", "Personalisation"),
        new("Start", "ms-settings:personalization-start", "start menu pinned recommended", "Personalisation"),
        new("Taskbar", "ms-settings:taskbar", "taskbar system tray corner icons", "Personalisation"),
        new("Fonts", "ms-settings:fonts", "install font typeface", "Personalisation"),
        new("Device usage", "ms-settings:deviceusage", "personalise usage", "Personalisation"),

        // ---- Apps ----
        new("Installed apps", "ms-settings:appsfeatures", "uninstall programs remove app", "Apps"),
        new("Default apps", "ms-settings:defaultapps", "default browser file associations open with", "Apps"),
        new("Startup apps", "ms-settings:startupapps", "launch at login boot autostart", "Apps"),
        new("Offline maps", "ms-settings:maps", "maps download", "Apps"),
        new("Apps for websites", "ms-settings:appsforwebsites", "deep links open in app", "Apps"),
        new("Video playback", "ms-settings:videoplayback", "hdr streaming video", "Apps"),
        new("Optional app features", "ms-settings:appsfeatures-app", "app features", "Apps"),

        // ---- Accounts ----
        new("Your info", "ms-settings:yourinfo", "account profile picture microsoft account", "Accounts"),
        new("Email & accounts", "ms-settings:emailandaccounts", "mail add account", "Accounts"),
        new("Sign-in options", "ms-settings:signinoptions", "password pin hello fingerprint face", "Accounts"),
        new("Windows Hello face", "ms-settings:signinoptions-launchfaceenrollment", "face recognition biometrics", "Accounts"),
        new("Windows Hello fingerprint", "ms-settings:signinoptions-launchfingerprintenrollment", "fingerprint biometrics", "Accounts"),
        new("Dynamic lock", "ms-settings:signinoptions-dynamiclock", "auto lock phone", "Accounts"),
        new("Family & other users", "ms-settings:otherusers", "add user child account", "Accounts"),
        new("Windows Backup", "ms-settings:backup", "sync settings backup", "Accounts"),
        new("Access work or school", "ms-settings:workplace", "azure ad domain join", "Accounts"),

        // ---- Time & language ----
        new("Date & time", "ms-settings:dateandtime", "clock timezone sync time", "Time & language"),
        new("Language & region", "ms-settings:regionlanguage", "locale keyboard layout country", "Time & language"),
        new("Typing / language options", "ms-settings:regionlanguage-languageoptions", "input method ime", "Time & language"),
        new("Speech", "ms-settings:speech", "voice recognition text to speech", "Time & language"),

        // ---- Gaming ----
        new("Game Bar", "ms-settings:gaming-gamebar", "xbox overlay record", "Gaming"),
        new("Captures", "ms-settings:gaming-gamedvr", "game recording clips screenshots", "Gaming"),
        new("Game Mode", "ms-settings:gaming-gamemode", "performance games", "Gaming"),

        // ---- Accessibility ----
        new("Accessibility", "ms-settings:easeofaccess", "ease of access", "Accessibility"),
        new("Text size", "ms-settings:easeofaccess-display", "bigger text scaling", "Accessibility"),
        new("Visual effects", "ms-settings:easeofaccess-visualeffects", "animations transparency scrollbars", "Accessibility"),
        new("Mouse pointer & touch", "ms-settings:easeofaccess-mousepointer", "cursor size colour", "Accessibility"),
        new("Text cursor", "ms-settings:easeofaccess-cursor", "caret indicator thickness", "Accessibility"),
        new("Magnifier", "ms-settings:easeofaccess-magnifier", "zoom screen", "Accessibility"),
        new("Colour filters", "ms-settings:easeofaccess-colorfilter", "colour blindness greyscale", "Accessibility"),
        new("Contrast themes", "ms-settings:easeofaccess-highcontrast", "high contrast", "Accessibility"),
        new("Narrator", "ms-settings:easeofaccess-narrator", "screen reader", "Accessibility"),
        new("Audio (mono)", "ms-settings:easeofaccess-audio", "mono audio", "Accessibility"),
        new("Captions", "ms-settings:easeofaccess-closedcaptioning", "subtitles live captions", "Accessibility"),
        new("Voice access", "ms-settings:easeofaccess-speechrecognition", "voice control dictation", "Accessibility"),
        new("Keyboard accessibility", "ms-settings:easeofaccess-keyboard", "sticky keys filter keys on-screen keyboard", "Accessibility"),
        new("Eye control", "ms-settings:easeofaccess-eyecontrol", "eye tracking", "Accessibility"),

        // ---- Privacy & security ----
        new("Privacy & security", "ms-settings:privacy", "privacy permissions", "Privacy"),
        new("Windows Security", "ms-settings:windowsdefender", "antivirus defender firewall threats", "Privacy"),
        new("Find my device", "ms-settings:findmydevice", "locate laptop", "Privacy"),
        new("Windows Update", "ms-settings:windowsupdate", "update patches upgrade check for updates", "Privacy"),
        new("Update history", "ms-settings:windowsupdate-history", "installed updates uninstall update", "Privacy"),
        new("Delivery optimisation", "ms-settings:delivery-optimization", "update bandwidth peer", "Privacy"),
        new("Windows Insider", "ms-settings:windowsinsider", "beta dev channel preview", "Privacy"),
        new("For developers", "ms-settings:developers", "developer mode ssh sudo terminal", "Privacy"),
        new("Device encryption", "ms-settings:deviceencryption", "bitlocker encrypt drive", "Privacy"),
        new("Location", "ms-settings:privacy-location", "gps location permission", "Privacy"),
        new("Camera privacy", "ms-settings:privacy-webcam", "camera permission apps", "Privacy"),
        new("Microphone privacy", "ms-settings:privacy-microphone", "mic permission apps", "Privacy"),
        new("Notifications privacy", "ms-settings:privacy-notifications", "notification access", "Privacy"),
        new("Account info privacy", "ms-settings:privacy-accountinfo", "account permission", "Privacy"),
        new("Contacts privacy", "ms-settings:privacy-contacts", "contacts permission", "Privacy"),
        new("Calendar privacy", "ms-settings:privacy-calendar", "calendar permission", "Privacy"),
        new("Diagnostics & feedback", "ms-settings:privacy-feedback", "telemetry data collection", "Privacy"),
        new("Activity history", "ms-settings:privacy-activityhistory", "timeline history", "Privacy"),
        new("Search permissions", "ms-settings:search-permissions", "safe search cloud content", "Privacy"),
        new("Searching Windows", "ms-settings:cortana-windowssearch", "indexing search index", "Privacy"),
        new("App permissions: file system", "ms-settings:privacy-broadfilesystemaccess", "file access permission", "Privacy"),

        // ---- Classic control panels & tools ----
        new("Device Manager", "devmgmt.msc", "drivers hardware devices", "Tools"),
        new("Disk Management", "diskmgmt.msc", "partitions volumes format drive", "Tools"),
        new("Services", "services.msc", "windows services start stop", "Tools"),
        new("Task Scheduler", "taskschd.msc", "scheduled tasks cron", "Tools"),
        new("Event Viewer", "eventvwr.msc", "logs errors system log", "Tools"),
        new("Performance Monitor", "perfmon.msc", "perfmon counters", "Tools"),
        new("Computer Management", "compmgmt.msc", "management console", "Tools"),
        new("Local Group Policy Editor", "gpedit.msc", "group policy gpo", "Tools"),
        new("Local Users and Groups", "lusrmgr.msc", "users groups local accounts", "Tools"),
        new("Registry Editor", "regedit.exe", "registry regedit hive", "Tools"),
        new("Task Manager", "taskmgr.exe", "processes cpu memory kill", "Tools"),
        new("System Configuration", "msconfig.exe", "msconfig boot startup services", "Tools"),
        new("System Information", "msinfo32.exe", "specs system info", "Tools"),
        new("Resource Monitor", "resmon.exe", "resources disk network cpu", "Tools"),
        new("DirectX Diagnostics", "dxdiag.exe", "dxdiag graphics diagnostics", "Tools"),
        new("Disk Cleanup", "cleanmgr.exe", "free space temp files", "Tools"),
        new("Character Map", "charmap.exe", "unicode symbols", "Tools"),
        new("On-Screen Keyboard", "osk.exe", "virtual keyboard", "Tools"),
        new("Snipping Tool", "ms-screenclip:", "screenshot capture screen", "Tools"),
        new("Control Panel", "control.exe", "control panel classic", "Tools"),
        new("Programs and Features", "appwiz.cpl", "uninstall programs add remove", "Tools"),
        new("Network Connections", "ncpa.cpl", "adapters ip network connections", "Tools"),
        new("Sound Control Panel", "mmsys.cpl", "playback recording devices audio", "Tools"),
        new("Power Options (classic)", "powercfg.cpl", "power plan high performance", "Tools"),
        new("System Properties", "sysdm.cpl", "computer name remote hardware", "Tools"),
        new("Internet Options", "inetcpl.cpl", "internet properties proxy certificates", "Tools"),
        new("Region (classic)", "intl.cpl", "region format locale", "Tools"),
        new("Mouse Properties", "main.cpl", "mouse buttons pointers", "Tools"),
        new("Date and Time (classic)", "timedate.cpl", "clock timezone", "Tools"),
        new("User Accounts (classic)", "netplwiz.exe", "auto login users password", "Tools"),
        new("Firewall (classic)", "firewall.cpl", "windows firewall rules", "Tools"),
        new("Credential Manager", "control.exe /name Microsoft.CredentialManager", "saved passwords credentials", "Tools"),
        new("Environment Variables", "rundll32.exe sysdm.cpl,EditEnvironmentVariables", "path env variables", "Tools"),
        new("All Tasks (God Mode)", "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}", "god mode every setting", "Tools"),
        new("Startup folder", "shell:startup", "autostart folder", "Tools"),
        new("Windows Tools", "shell:::{D20EA4E1-3957-11d2-A40B-0C5020524153}", "administrative tools", "Tools"),
        new("Fonts folder", "shell:fonts", "installed fonts", "Tools"),
        new("Temp folder", "shell:::{BBCBDE7A-9AA3-4E1E-A2AF-1D0AB0AB1D2C}", "temporary files", "Tools"),
    };
}
