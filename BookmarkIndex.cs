using System.IO;
using System.Text.Json;

namespace Winfred;

/// <summary>
/// Reads bookmarks straight out of the Chromium "Bookmarks" JSON file, for every
/// installed Chromium-family browser and profile. Nothing is written back.
/// </summary>
public sealed class BookmarkIndex
{
    public sealed record Bookmark(string Title, string Url, string Folder, string Browser, string Profile, DateTime Added);

    public sealed record Source(string Browser, string Profile, string BookmarksFile);

    private readonly object _lock = new();
    private List<Bookmark> _bookmarks = new();
    private DateTime _loadedAt;
    private string? _error;

    public int Count { get { lock (_lock) return _bookmarks.Count; } }
    public string? Error => _error;
    public DateTime LoadedAt => _loadedAt;

    /// <summary>Where each browser keeps its user-data root.</summary>
    private static readonly (string Name, string Root)[] BrowserRoots =
    {
        ("Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data"),
        ("Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data"),
        ("Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data"),
        ("Vivaldi", @"%LOCALAPPDATA%\Vivaldi\User Data"),
        ("Chromium", @"%LOCALAPPDATA%\Chromium\User Data"),
        ("Opera", @"%APPDATA%\Opera Software\Opera Stable"),
        ("Opera GX", @"%APPDATA%\Opera Software\Opera GX Stable"),
    };

    /// <summary>Every browser/profile that actually has a bookmarks file on this machine.</summary>
    public static List<Source> DetectSources()
    {
        var sources = new List<Source>();
        foreach (var (name, rawRoot) in BrowserRoots)
        {
            string root = Environment.ExpandEnvironmentVariables(rawRoot);
            if (!Directory.Exists(root)) continue;

            // Opera keeps Bookmarks in the root; Chrome/Edge use per-profile subfolders.
            string rootFile = Path.Combine(root, "Bookmarks");
            if (File.Exists(rootFile)) sources.Add(new Source(name, "Default", rootFile));

            IEnumerable<string> profiles;
            try { profiles = Directory.EnumerateDirectories(root); }
            catch { continue; }

            foreach (string profile in profiles)
            {
                string file = Path.Combine(profile, "Bookmarks");
                if (File.Exists(file))
                    sources.Add(new Source(name, Path.GetFileName(profile), file));
            }
        }
        return sources;
    }

    public static List<string> DetectBrowsers() =>
        DetectSources().Select(s => s.Browser).Distinct().ToList();

    /// <summary>The profile a browser opens by default — everything else is a side profile.</summary>
    public static bool IsDefaultProfile(string profile) =>
        profile.Equals("Default", StringComparison.OrdinalIgnoreCase);

    public void Refresh(bool force = false)
    {
        var cfg = Config.Current.Bookmarks;
        if (!cfg.Enabled)
        {
            lock (_lock) _bookmarks = new List<Bookmark>();
            return;
        }
        if (!force && DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(Math.Clamp(cfg.RefreshMinutes, 1, 240)))
            return;

        try
        {
            var collected = new List<Bookmark>();
            foreach (var source in DetectSources())
            {
                if (cfg.Browsers.Count > 0 &&
                    !cfg.Browsers.Any(b => b.Equals(source.Browser, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (cfg.DefaultProfileOnly && !IsDefaultProfile(source.Profile))
                    continue;
                ReadFile(source, collected);
            }
            lock (_lock) _bookmarks = collected;
            _error = collected.Count == 0 ? "No bookmarks found in the selected browsers." : null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        _loadedAt = DateTime.UtcNow;
    }

    private static void ReadFile(Source source, List<Bookmark> output)
    {
        try
        {
            // Chromium holds no lock on this file, but copy semantics keep us safe on a write.
            string json = File.ReadAllText(source.BookmarksFile);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("roots", out var roots)) return;
            foreach (var root in roots.EnumerateObject())
            {
                if (root.Value.ValueKind != JsonValueKind.Object) continue;
                Walk(root.Value, FolderLabel(root.Name), source, output, isRoot: true);
            }
        }
        catch
        {
            // an unreadable or half-written profile is skipped, not fatal
        }
    }

    private static string FolderLabel(string rootName) => rootName switch
    {
        "bookmark_bar" => "Bookmarks bar",
        "other" => "Other bookmarks",
        "synced" => "Mobile bookmarks",
        _ => rootName,
    };

    private static void Walk(JsonElement node, string folder, Source source, List<Bookmark> output,
        bool isRoot = false)
    {
        if (output.Count > 50_000) return;

        string type = node.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        string name = node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

        if (type == "url")
        {
            string url = node.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            if (url.Length == 0 || url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return;
            output.Add(new Bookmark(name.Length > 0 ? name : url, url, folder,
                source.Browser, source.Profile, ChromeTime(node)));
            return;
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) return;
        // The root node repeats its own name ("Bookmarks bar"), which the label already carries.
        string childFolder = !isRoot && type == "folder" && name.Length > 0 && folder.Length > 0
            ? $"{folder} / {name}"
            : folder;
        foreach (var child in children.EnumerateArray())
            Walk(child, childFolder, source, output);
    }

    /// <summary>Chromium stores microseconds since 1601-01-01 UTC.</summary>
    private static DateTime ChromeTime(JsonElement node)
    {
        try
        {
            if (node.TryGetProperty("date_added", out var added) &&
                long.TryParse(added.GetString(), out long microseconds) && microseconds > 0)
                return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(microseconds * 10);
        }
        catch { /* fall through */ }
        return DateTime.MinValue;
    }

    public List<Bookmark> Search(string query, int limit)
    {
        Refresh();
        List<Bookmark> snapshot;
        lock (_lock) snapshot = _bookmarks;

        string trimmed = query.Trim();
        if (trimmed.Length == 0)
            return snapshot.OrderByDescending(b => b.Added).Take(limit).ToList();

        var terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return snapshot
            .Select(b => (b, score: ScoreBookmark(b, terms)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => x.b)
            .ToList();
    }

    private static double ScoreBookmark(Bookmark bookmark, string[] terms)
    {
        double total = 0;
        foreach (var term in terms)
        {
            double score = Math.Max(
                FuzzyMatcher.Score(bookmark.Title, term),
                FuzzyMatcher.Score(HostOf(bookmark.Url), term) * 0.9);
            if (score <= 0 && bookmark.Url.Contains(term, StringComparison.OrdinalIgnoreCase))
                score = 15;
            if (score <= 0) return 0;
            total += score;
        }
        return total / terms.Length;
    }

    public static string HostOf(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
    }
}
