using System.Diagnostics;
using System.IO;

namespace Winfred;

/// <summary>
/// Routes a query: calculator → keyword triggers (search shortcuts, files, bookmarks,
/// notes, settings, 1Password) → default results merged by relevance → web fallback.
/// </summary>
public sealed class SearchEngine : IDisposable
{
    private readonly OnePasswordProvider _onePassword = new();

    public ApplicationIndex Applications { get; } = new();
    public FileIndexer Files { get; } = new();
    public BookmarkIndex Bookmarks { get; } = new();
    public EvernoteIndex Evernote { get; } = new();

    /// <summary>Opens the settings window; wired up by App so the engine stays UI-agnostic.</summary>
    public static Action? OpenSettings;
    public event Action? ResultsChanged;

    public void Start()
    {
        Applications.Changed += OnApplicationsChanged;
        Applications.Start();
        Files.Start();
        // Warm the slower sources up front so the first keyword query is instant.
        Task.Run(() =>
        {
            Bookmarks.Refresh(force: true);
            Evernote.EnsureLoaded();
        });
    }

    public void Reconfigure()
    {
        Files.Reconfigure();
        Task.Run(() =>
        {
            Bookmarks.Refresh(force: true);
            Evernote.EnsureLoaded(force: true);
        });
    }

    /// <param name="requery">Called (on any thread) when async results arrive and the
    /// query should be re-run.</param>
    public List<ResultItem> Query(string raw, Action requery)
    {
        var text = raw.Trim();
        var cfg = Config.Current;
        int limit = cfg.Appearance.MaxResults;

        // Alfred shows only the search field until you type; the placeholder covers onboarding.
        if (text.Length == 0) return new List<ResultItem>();

        var results = new List<ResultItem>();

        // 1. The calculator always gets first refusal, so "10+25" answers instantly.
        if (cfg.Calculator.Enabled)
        {
            var calculated = Calculator.TryEvaluate(text);
            if (calculated != null) results.Add(CalculatorResult(calculated));
        }

        int space = text.IndexOf(' ');
        string head = space < 0 ? text : text[..space];
        string rest = space < 0 ? "" : text[(space + 1)..].Trim();

        // 2. Keyword triggers own the whole query when they match.
        var keyworded = RouteKeyword(head, rest, space < 0, requery, limit);
        if (keyworded != null)
        {
            results.AddRange(keyworded);
            return Finish(results, text, limit + results.Count(r => r.Priority >= 100));
        }

        // 3. Default results — everything that opted into unprefixed search.
        if (LooksLikeUrl(text))
        {
            string url = text.Contains("://") ? text : "https://" + text;
            results.Add(new ResultItem
            {
                Icon = Glyphs.Globe,
                Title = $"Open {url}",
                Subtitle = "Open in your default browser",
                Kind = ResultKind.Web,
                Priority = 90,
                Uid = "url:" + url,
                Enter = ResultAction.Of("open", () => OpenUrl(url)),
                CtrlEnter = ResultAction.Of("copy", () => ClipboardGuard.CopyPlain(url)),
            });
        }

        results.AddRange(ApplicationResults(text, Math.Min(5, limit)));
        if (cfg.Files.Enabled && cfg.Files.InDefaultResults)
            results.AddRange(FileResults(text, Math.Min(4, limit)));
        if (cfg.Bookmarks.Enabled && cfg.Bookmarks.InDefaultResults)
            results.AddRange(BookmarkResults(text, 3));
        if (cfg.Evernote.Enabled && cfg.Evernote.InDefaultResults)
            results.AddRange(NoteResults(text, 3, requery));
        if (cfg.SystemSettings.Enabled && cfg.SystemSettings.InDefaultResults)
            results.AddRange(SettingsResults(text, 3));

        results.AddRange(BuiltInActions(text));

        // 4. Web search fallback, always available at the bottom.
        results.Add(new ResultItem
        {
            Icon = Glyphs.Search,
            Title = $"Search {cfg.DefaultSearchName} for “{text}”",
            Subtitle = "Press Enter to open in your browser",
            Kind = ResultKind.Web,
            Score = 5,
            Uid = "search:default",
            Enter = ResultAction.Of("search",
                () => OpenUrl(cfg.DefaultSearchUrl.Replace("{q}", Uri.EscapeDataString(text)))),
        });

        // 5. Suggest keywords the user is part-way through typing.
        if (space < 0) results.AddRange(KeywordSuggestions(head, cfg));

        return Finish(results, text, limit);
    }

    private List<ResultItem> Finish(List<ResultItem> results, string query, int limit)
    {
        foreach (var item in results)
            item.Score += Usage.Boost(query, item.Uid);

        return results
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    // ---------- keyword routing ----------

    private List<ResultItem>? RouteKeyword(string head, string rest, bool bare, Action requery, int limit)
    {
        var cfg = Config.Current;

        if (cfg.OnePassword.Enabled && Equal(head, cfg.OnePassword.Trigger))
            return _onePassword.GetResults(rest, requery);

        if (cfg.Files.Enabled && Equal(head, cfg.Files.Keyword))
            return WithPrompt(FileResults(rest, limit), bare, "Search indexed files",
                $"{Files.Count:N0} items indexed — keep typing a file name");

        if (cfg.Files.Enabled && Equal(head, cfg.Files.RevealKeyword))
            return WithPrompt(FileResults(rest, limit, revealFirst: true), bare, "Reveal a file in Explorer",
                "Enter opens the containing folder with the file selected");

        if (cfg.Bookmarks.Enabled && Equal(head, cfg.Bookmarks.Keyword))
            return WithPrompt(BookmarkResults(rest, limit), bare, "Search browser bookmarks",
                $"{Bookmarks.Count:N0} bookmarks from {string.Join(", ", cfg.Bookmarks.Browsers)}");

        if (cfg.Evernote.Enabled && Equal(head, cfg.Evernote.Keyword))
            return WithPrompt(NoteResults(rest, limit, requery), bare, "Search Evernote",
                Evernote.Count > 0 ? $"{Evernote.Count:N0} notes indexed" : "Indexing your notes…");

        if (cfg.SystemSettings.Enabled && Equal(head, cfg.SystemSettings.Keyword))
            return WithPrompt(SettingsResults(rest, limit), bare, "Search Windows settings",
                $"{SystemSettingsCatalog.Pages.Count} settings pages and control panels");

        if (cfg.Searches.TryGetValue(head, out var shortcut))
        {
            if (rest.Length > 0)
                return new List<ResultItem> { SearchItem(head, shortcut, rest) };
            return new List<ResultItem>
            {
                new()
                {
                    Icon = shortcut.Icon,
                    Title = $"Search {shortcut.Name}",
                    Subtitle = $"Keep typing: {head} <your search> — Enter opens {shortcut.Name}",
                    Kind = ResultKind.Web,
                    Priority = 50,
                    Enter = ResultAction.Of("open site", () => OpenUrl(HomepageOf(shortcut.Url))),
                },
            };
        }

        return null;
    }

    /// <summary>Shows a "keep typing" row when a keyword was typed with no argument yet.</summary>
    private static List<ResultItem> WithPrompt(List<ResultItem> results, bool bare, string title, string subtitle)
    {
        if (!bare) return results;
        var header = ResultItem.Info(Glyphs.Keyboard, title, subtitle);
        header.Score = 1000; // keep the "keep typing" row above the recent items beneath it
        var prompt = new List<ResultItem> { header };
        prompt.AddRange(results);
        return prompt;
    }

    private static bool Equal(string a, string b) =>
        b.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    // ---------- provider → result mapping ----------

    private List<ResultItem> ApplicationResults(string query, int limit)
    {
        var applications = Applications.Search(query, limit);
        var results = new List<ResultItem>();
        foreach (var application in applications)
        {
            results.Add(new ResultItem
            {
                Icon = Glyphs.App,
                IconImage = IconExtractor.ForApp(application),
                Title = application.Name,
                Subtitle = $"Application · {application.Location}",
                Kind = ResultKind.Application,
                Uid = "app:" + application.Target,
                Score = 50,
                Enter = ResultAction.Of("launch", () => ApplicationIndex.Launch(application)),
            });
        }
        return Rank(results, applications.Count);
    }

    private static ResultItem CalculatorResult(Calculator.Result calculated)
    {
        var cfg = Config.Current.Calculator;
        string subtitle = calculated.Alternates.Length > 0
            ? $"{calculated.Expression} · {calculated.Alternates}"
            : $"{calculated.Expression} — Enter copies the result";

        return new ResultItem
        {
            Icon = Glyphs.Calculator,
            Title = calculated.Formatted,
            Subtitle = subtitle,
            Kind = ResultKind.Calculator,
            Priority = 100,
            Enter = ResultAction.Of("copy", () =>
            {
                if (cfg.CopyOnEnter) ClipboardGuard.CopyPlain(calculated.Formatted);
                Notifier.Notify($"{calculated.Formatted} copied.");
            }),
            CtrlEnter = ResultAction.Of("copy “expr = result”",
                () => ClipboardGuard.CopyPlain($"{calculated.Expression} = {calculated.Formatted}")),
            ShiftEnter = cfg.ShowLargeType
                ? ResultAction.Of("large type", () => LargeTypeWindow.ShowText(calculated.Formatted), hidesWindow: false)
                : null,
        };
    }

    private List<ResultItem> FileResults(string query, int limit, bool revealFirst = false)
    {
        var entries = Files.Search(query, limit);
        var results = new List<ResultItem>();
        foreach (var entry in entries)
        {
            string folder = Path.GetDirectoryName(entry.Path) ?? entry.Path;
            string meta = entry.IsDirectory
                ? "Folder"
                : $"{FormatSize(entry.Size)} · {entry.Modified.ToLocalTime():d MMM yyyy}";

            var open = ResultAction.Of("open", () => OpenPath(entry.Path));
            var reveal = ResultAction.Of("reveal", () => Reveal(entry.Path));

            results.Add(new ResultItem
            {
                Icon = entry.IsDirectory ? Glyphs.Folder : IconForExtension(Path.GetExtension(entry.Path)),
                IconImage = IconExtractor.ForFile(entry.Path, entry.IsDirectory),
                Title = entry.Name,
                Subtitle = $"{meta} — {folder}",
                Kind = ResultKind.File,
                Uid = "file:" + entry.Path,
                Score = 40,
                AutoComplete = entry.Name,
                Enter = revealFirst ? reveal : open,
                CtrlEnter = revealFirst ? open : reveal,
                ShiftEnter = ResultAction.Of("copy path", () => ClipboardGuard.CopyPlain(entry.Path)),
                AltEnter = ResultAction.Of("open folder", () => OpenPath(folder)),
            });
        }
        return Rank(results, entries.Count);
    }

    private List<ResultItem> BookmarkResults(string query, int limit)
    {
        var results = new List<ResultItem>();
        var bookmarks = Bookmarks.Search(query, limit);
        foreach (var bookmark in bookmarks)
        {
            results.Add(new ResultItem
            {
                Icon = Glyphs.Star,
                Title = bookmark.Title,
                Subtitle = $"{bookmark.Browser} · {bookmark.Folder} — {bookmark.Url}",
                Kind = ResultKind.Bookmark,
                Uid = "bm:" + bookmark.Url,
                Score = 38,
                Enter = ResultAction.Of("open", () => OpenUrl(bookmark.Url)),
                CtrlEnter = ResultAction.Of("copy URL", () => ClipboardGuard.CopyPlain(bookmark.Url)),
            });
        }
        return Rank(results, bookmarks.Count);
    }

    private List<ResultItem> NoteResults(string query, int limit, Action requery)
    {
        var notes = Evernote.Search(query, limit, requery);
        var results = new List<ResultItem>();
        foreach (var note in notes)
        {
            results.Add(new ResultItem
            {
                Icon = Glyphs.Note,
                Title = note.Title,
                Subtitle = EvernoteIndex.Describe(note),
                Kind = ResultKind.Note,
                Uid = "en:" + note.Id,
                Score = 36,
                Enter = ResultAction.Of("open in Evernote", () => OpenUrl(note.AppLink)),
                CtrlEnter = ResultAction.Of("open on web", () => OpenUrl(note.WebLink)),
                ShiftEnter = ResultAction.Of("copy link", () => ClipboardGuard.CopyPlain(note.WebLink)),
            });
        }

        if (results.Count == 0 && Evernote.Error != null && query.Length > 0)
            results.Add(ResultItem.Info(Glyphs.Note, "Evernote", Evernote.Error, ResultKind.Error));
        return Rank(results, notes.Count);
    }

    private static List<ResultItem> SettingsResults(string query, int limit)
    {
        var pages = SystemSettingsCatalog.Search(query, limit);
        var results = new List<ResultItem>();
        foreach (var page in pages)
        {
            results.Add(new ResultItem
            {
                Icon = Glyphs.Settings,
                Title = page.Name,
                Subtitle = $"Windows {page.Group} · {page.Target}",
                Kind = ResultKind.Setting,
                Uid = "set:" + page.Target,
                Score = 34,
                Enter = ResultAction.Of("open", () => SystemSettingsCatalog.Open(page)),
                CtrlEnter = ResultAction.Of("copy command", () => ClipboardGuard.CopyPlain(page.Target)),
            });
        }
        return Rank(results, pages.Count);
    }

    /// <summary>Keeps each provider's own ordering by nudging scores down the list.</summary>
    private static List<ResultItem> Rank(List<ResultItem> results, int count)
    {
        for (int i = 0; i < results.Count; i++)
            results[i].Score += Math.Max(0, count - i);
        return results;
    }

    private List<ResultItem> BuiltInActions(string text)
    {
        var results = new List<ResultItem>();
        if (text.Length < 2) return results; // a single letter shouldn't summon these

        void Add(string title, string subtitle, string icon, Action run, params string[] triggers)
        {
            if (!triggers.Any(t => t.StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
                                   FuzzyMatcher.Score(title, text) > 90))
                return;
            results.Add(new ResultItem
            {
                Icon = icon,
                Title = title,
                Subtitle = subtitle,
                Kind = ResultKind.Action,
                Score = 45,
                Uid = "action:" + title,
                Enter = ResultAction.Of("run", run),
            });
        }

        Add("Winfred Settings", "Hotkey, search engines, files, notes, 1Password", Glyphs.Settings,
            () => OpenSettings?.Invoke(), "winfred", "settings", "preferences", "prefs", "config");
        Add("Rebuild file index", $"{Files.Count:N0} items currently indexed", Glyphs.Refresh,
            () => Files.Rebuild(), "reindex", "rebuild");
        Add("Reload Winfred config", Config.FilePath, Glyphs.Refresh,
            Config.Load, "reload");
        return results;
    }

    private static IEnumerable<ResultItem> KeywordSuggestions(string head, Config cfg)
    {
        if (head.Length == 0) yield break;

        foreach (var (keyword, shortcut) in cfg.Searches
                     .Where(p => p.Value.ShowInHints &&
                                 p.Key.StartsWith(head, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p.Key)
                     .Take(4))
        {
            yield return new ResultItem
            {
                Icon = shortcut.Icon,
                Title = $"{keyword} — search {shortcut.Name}",
                Subtitle = $"Type: {keyword} <your search>",
                Kind = ResultKind.Hint,
                Score = 2,
                AutoComplete = keyword + " ",
            };
        }

        foreach (var (keyword, label, enabled) in ProviderKeywords(cfg))
        {
            if (!enabled || !keyword.StartsWith(head, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new ResultItem
            {
                Icon = Glyphs.Keyboard,
                Title = $"{keyword} — {label}",
                Subtitle = $"Type: {keyword} <your search>",
                Kind = ResultKind.Hint,
                Score = 2,
                AutoComplete = keyword + " ",
            };
        }
    }

    private static IEnumerable<(string Keyword, string Label, bool Enabled)> ProviderKeywords(Config cfg)
    {
        yield return (cfg.Files.Keyword, "search files", cfg.Files.Enabled);
        yield return (cfg.Files.RevealKeyword, "reveal a file in Explorer", cfg.Files.Enabled);
        yield return (cfg.Bookmarks.Keyword, "search bookmarks", cfg.Bookmarks.Enabled);
        yield return (cfg.Evernote.Keyword, "search Evernote notes", cfg.Evernote.Enabled);
        yield return (cfg.SystemSettings.Keyword, "Windows settings", cfg.SystemSettings.Enabled);
        yield return (cfg.OnePassword.Trigger, "1Password items", cfg.OnePassword.Enabled);
    }

    private static ResultItem SearchItem(string keyword, SearchShortcut shortcut, string terms) => new()
    {
        Icon = shortcut.Icon,
        Title = $"Search {shortcut.Name} for “{terms}”",
        Subtitle = "Press Enter to open in your browser",
        Kind = ResultKind.Web,
        Priority = 50,
        Uid = "search:" + keyword,
        Enter = ResultAction.Of("search",
            () => OpenUrl(shortcut.Url.Replace("{q}", Uri.EscapeDataString(terms)))),
        CtrlEnter = ResultAction.Of("copy URL",
            () => ClipboardGuard.CopyPlain(shortcut.Url.Replace("{q}", Uri.EscapeDataString(terms)))),
    };

    // ---------- helpers ----------

    private static string HomepageOf(string urlTemplate)
    {
        try
        {
            return new Uri(urlTemplate.Replace("{q}", "")).GetLeftPart(UriPartial.Authority);
        }
        catch
        {
            return urlTemplate;
        }
    }

    public static bool LooksLikeUrl(string text)
    {
        if (text.Contains(' ')) return false;
        if (text.StartsWith("http://") || text.StartsWith("https://")) return true;
        int dot = text.IndexOf('.');
        if (dot <= 0 || dot == text.Length - 1 || text.Contains("..")) return false;
        return Uri.CheckHostName(text.Split('/')[0]) != UriHostNameType.Unknown;
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifier.Notify($"Couldn't open {url}: {ex.Message}");
        }
    }

    public static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifier.Notify($"Couldn't open {path}: {ex.Message}");
        }
    }

    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifier.Notify($"Couldn't reveal {path}: {ex.Message}");
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    private static string IconForExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => Glyphs.Pdf,
        ".doc" or ".docx" or ".odt" or ".rtf" => Glyphs.Document,
        ".xls" or ".xlsx" or ".csv" or ".ods" => Glyphs.Spreadsheet,
        ".ppt" or ".pptx" or ".odp" => Glyphs.Video,
        ".txt" or ".md" or ".log" => Glyphs.Document,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".heic" => Glyphs.Picture,
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => Glyphs.Music,
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => Glyphs.Video,
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => Glyphs.Zip,
        ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => Glyphs.Settings,
        ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".c" or ".go" or ".rs" or ".rb" => Glyphs.Code,
        ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" => Glyphs.Code,
        _ => Glyphs.Document,
    };

    private void OnApplicationsChanged() => ResultsChanged?.Invoke();

    public void Dispose()
    {
        Applications.Changed -= OnApplicationsChanged;
        Files.Dispose();
    }
}
