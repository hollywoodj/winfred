using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winfred;

public class HotkeyConfig
{
    /// <summary>doubleTap | combo</summary>
    public string Mode { get; set; } = "doubleTap";

    /// <summary>win | ctrl | alt | shift — "win" is the Command key on a Mac keyboard.</summary>
    public string Key { get; set; } = "win";

    public int DoubleTapMs { get; set; } = 350;

    /// <summary>Used when Mode is "combo", e.g. "Ctrl+Space" or "Alt+Shift+K".</summary>
    public string Combo { get; set; } = "Ctrl+Space";

    /// <summary>
    /// Ignore the hotkey while a full-screen app owns the foreground. Taking focus from a game
    /// makes it mute itself, and over exclusive full-screen the launcher never appears anyway,
    /// so the summon would cost a second of silence and show nothing.
    /// </summary>
    public bool SuppressInFullscreen { get; set; } = true;

    public string Describe() => Mode.Equals("combo", StringComparison.OrdinalIgnoreCase)
        ? Combo
        : $"double-tap {Key}";
}

public class SearchShortcut
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = ""; // {q} is replaced with the URL-encoded query
    public string Icon { get; set; } = "🔍";

    /// <summary>Shown in the results when the keyword is typed with no query yet.</summary>
    public bool ShowInHints { get; set; } = true;
}

public class AppearanceConfig
{
    public int MaxResults { get; set; } = 9;
    public double WindowWidth { get; set; } = 700;
    /// <summary>Vertical position of the launcher as a percentage of the working area.</summary>
    public double TopOffsetPercent { get; set; } = 22;
    /// <summary>dark | midnight | light</summary>
    public string Theme { get; set; } = "dark";
    public bool ShowActionHints { get; set; } = true;
    public double FontScale { get; set; } = 1.0;
    public string Placeholder { get; set; } = "Search applications, the web, files, notes, “10+25” …";
}

public class CalculatorConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>Maximum decimal places shown; trailing zeros are trimmed.</summary>
    public int Decimals { get; set; } = 6;
    public bool ThousandsSeparator { get; set; } = true;
    /// <summary>When true the calculator only fires for queries starting with "=".</summary>
    public bool RequireEqualsPrefix { get; set; }
    public bool CopyOnEnter { get; set; } = true;
    public bool ShowLargeType { get; set; } = true;
}

public class FileScope
{
    public string Path { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
}

public class FileSearchConfig
{
    public bool Enabled { get; set; } = true;
    public string Keyword { get; set; } = "open";
    public string RevealKeyword { get; set; } = "find";
    public bool InDefaultResults { get; set; } = true;
    public List<FileScope> Scopes { get; set; } = DefaultScopes();
    public List<string> ExcludedFolders { get; set; } = new()
    {
        "node_modules", ".git", ".svn", "obj", "bin", "AppData", "$RECYCLE.BIN",
        "System Volume Information", ".venv", "venv", "__pycache__", ".next", ".cache",
    };
    public List<string> ExcludedExtensions { get; set; } = new()
    {
        ".tmp", ".log", ".lock", ".pyc", ".obj", ".pdb", ".dll.config",
    };
    /// <summary>When non-empty, only these extensions are indexed.</summary>
    public List<string> IncludedExtensions { get; set; } = new();
    public bool IndexHidden { get; set; }
    public bool IndexFolders { get; set; } = true;
    public int MaxEntries { get; set; } = 300_000;
    public int RefreshMinutes { get; set; } = 60;
    public bool LiveWatch { get; set; } = true;

    public static List<FileScope> DefaultScopes()
    {
        var scopes = new List<FileScope>();
        void Add(Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length > 0 && Directory.Exists(path))
                scopes.Add(new FileScope { Path = path });
        }
        Add(Environment.SpecialFolder.Desktop);
        Add(Environment.SpecialFolder.MyDocuments);
        var downloads = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
            scopes.Add(new FileScope { Path = downloads });
        return scopes;
    }
}

public class BookmarksConfig
{
    public bool Enabled { get; set; } = true;
    public string Keyword { get; set; } = "bm";
    public bool InDefaultResults { get; set; } = true;
    /// <summary>Chromium browsers to read bookmarks from. Empty = every one detected.</summary>
    public List<string> Browsers { get; set; } = new() { "Brave" };

    /// <summary>Skip secondary profiles ("Guest Profile", "Profile 2", …) and read only "Default".</summary>
    public bool DefaultProfileOnly { get; set; } = true;

    public int RefreshMinutes { get; set; } = 5;
}

public class EvernoteConfig
{
    public bool Enabled { get; set; } = true;
    public string Keyword { get; set; } = "en";
    public bool InDefaultResults { get; set; }
    /// <summary>Empty = auto-detect the Evernote v10 conduit database.</summary>
    public string DatabasePath { get; set; } = "";
    public bool SearchContent { get; set; } = true;
    public bool IncludeTrashed { get; set; }
    public int RefreshMinutes { get; set; } = 10;
}

public class SystemSettingsConfig
{
    public bool Enabled { get; set; } = true;
    public string Keyword { get; set; } = "set";
    public bool InDefaultResults { get; set; } = true;
}

public class OnePasswordConfig
{
    public bool Enabled { get; set; } = true;
    public string Trigger { get; set; } = "1p";
    public string CliPath { get; set; } = "op";

    /// <summary>
    /// How a Login item is filled after its site opens:
    /// extension — press the 1Password browser-extension autofill shortcut;
    /// type — type the username/password directly;
    /// copyOnly — never fill, just copy the password.
    /// </summary>
    public string FillMode { get; set; } = "extension";

    public bool SubmitAfterFill { get; set; } = true;
    /// <summary>How long to wait for the site to load before filling.</summary>
    public int PageLoadMs { get; set; } = 2500;
    /// <summary>Wait after the fill before pressing Enter to submit.</summary>
    public int SubmitDelayMs { get; set; } = 900;
    public string ExtensionShortcut { get; set; } = "Ctrl+\\";
    /// <summary>Empty = the system default browser.</summary>
    public string BrowserPath { get; set; } = "";
    public int CacheMinutes { get; set; } = 5;
    public int ClipboardClearSeconds { get; set; } = 45;

    /// <summary>1Password categories that are copied rather than filled.</summary>
    public List<string> CopyCategories { get; set; } = new()
    {
        "PASSWORD", "API_CREDENTIAL", "DATABASE", "SERVER", "SSH_KEY",
        "SECURE_NOTE", "CREDIT_CARD", "WIRELESS_ROUTER", "SOFTWARE_LICENSE",
    };

    public bool IsCopyCategory(string? category) =>
        category != null && CopyCategories.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
}

public class Config
{
    public HotkeyConfig Hotkey { get; set; } = new();
    public string DefaultSearchName { get; set; } = "Google";
    public string DefaultSearchUrl { get; set; } = "https://www.google.com/search?q={q}";
    public Dictionary<string, SearchShortcut> Searches { get; set; } = DefaultSearches();
    public AppearanceConfig Appearance { get; set; } = new();
    public CalculatorConfig Calculator { get; set; } = new();
    public FileSearchConfig Files { get; set; } = new();
    public BookmarksConfig Bookmarks { get; set; } = new();
    public EvernoteConfig Evernote { get; set; } = new();
    public SystemSettingsConfig SystemSettings { get; set; } = new();
    public OnePasswordConfig OnePassword { get; set; } = new();
    /// <summary>Rank results the user picks often a little higher.</summary>
    public bool LearnFromUsage { get; set; } = true;

    public static Config Current { get; private set; } = new();

    /// <summary>Raised on the UI thread after the config is replaced or saved.</summary>
    public static event Action? Changed;

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Winfred");

    public static string FilePath => Path.Combine(Directory, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), JsonOpts);
                if (loaded != null)
                {
                    Current = Normalize(loaded);
                    Changed?.Invoke();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Notifier.Notify($"Config could not be read, using defaults: {ex.Message}");
            Current = Normalize(new Config());
            Changed?.Invoke();
            return; // don't overwrite a config the user may be able to fix
        }
        Current = Normalize(new Config());
        Save();
    }

    public static void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
        Changed?.Invoke();
    }

    /// <summary>Replaces the live config (used by the settings window) and persists it.</summary>
    public static void Replace(Config config)
    {
        Current = Normalize(config);
        Save();
    }

    /// <summary>A deep copy, so the settings window can edit without touching the live config.</summary>
    public Config Clone() =>
        Normalize(JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts)!);

    private static Config Normalize(Config config)
    {
        config.Searches = new Dictionary<string, SearchShortcut>(config.Searches, StringComparer.OrdinalIgnoreCase);
        config.Appearance.MaxResults = Math.Clamp(config.Appearance.MaxResults, 3, 20);
        config.Appearance.WindowWidth = Math.Clamp(config.Appearance.WindowWidth, 480, 1400);
        config.Appearance.TopOffsetPercent = Math.Clamp(config.Appearance.TopOffsetPercent, 0, 60);
        config.Appearance.FontScale = Math.Clamp(config.Appearance.FontScale, 0.8, 1.6);
        config.Calculator.Decimals = Math.Clamp(config.Calculator.Decimals, 0, 12);
        config.Files.MaxEntries = Math.Clamp(config.Files.MaxEntries, 1000, 2_000_000);
        return config;
    }

    public static Dictionary<string, SearchShortcut> DefaultSearches() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["goog"] = new() { Name = "Google", Url = "https://www.google.com/search?q={q}" },
        ["yt"] = new() { Name = "YouTube", Url = "https://www.youtube.com/results?search_query={q}", Icon = "▶️" },
        ["gh"] = new() { Name = "GitHub", Url = "https://github.com/search?q={q}", Icon = "🐙" },
        ["wiki"] = new() { Name = "Wikipedia", Url = "https://en.wikipedia.org/wiki/Special:Search?search={q}", Icon = "📖" },
        ["maps"] = new() { Name = "Google Maps", Url = "https://www.google.com/maps/search/{q}", Icon = "🗺️" },
        ["amzn"] = new() { Name = "Amazon", Url = "https://www.amazon.com/s?k={q}", Icon = "📦" },
        ["ddg"] = new() { Name = "DuckDuckGo", Url = "https://duckduckgo.com/?q={q}", Icon = "🦆" },
        ["img"] = new() { Name = "Google Images", Url = "https://www.google.com/search?tbm=isch&q={q}", Icon = "🖼️" },
        ["so"] = new() { Name = "Stack Overflow", Url = "https://stackoverflow.com/search?q={q}", Icon = "💬" },
        ["tr"] = new() { Name = "Google Translate", Url = "https://translate.google.com/?text={q}", Icon = "🌐" },
    };
}
