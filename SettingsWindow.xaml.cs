using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace Winfred;

public partial class SettingsWindow : Window
{
    /// <summary>The config being edited — a clone, so Cancel really cancels.</summary>
    private Config _draft = Config.Current.Clone();

    private readonly SearchEngine _engine;
    private readonly ObservableCollection<SearchRow> _searches = new();
    private readonly ObservableCollection<ScopeRow> _scopes = new();
    private readonly List<CheckBox> _browserBoxes = new();
    private readonly List<CheckBox> _categoryBoxes = new();

    private static readonly string[] KnownCategories =
    {
        "LOGIN", "PASSWORD", "API_CREDENTIAL", "SECURE_NOTE", "CREDIT_CARD", "IDENTITY",
        "SSH_KEY", "DATABASE", "SERVER", "WIRELESS_ROUTER", "SOFTWARE_LICENSE", "BANK_ACCOUNT",
        "MEMBERSHIP", "PASSPORT", "DRIVER_LICENSE", "EMAIL_ACCOUNT",
    };

    public sealed class SearchRow
    {
        public string Keyword { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Icon { get; set; } = Glyphs.Search;
        public bool ShowInHints { get; set; } = true;
    }

    public sealed class ScopeRow
    {
        public string Path { get; set; } = "";
        public bool IncludeSubfolders { get; set; } = true;
    }

    public SettingsWindow(SearchEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        SearchesGrid.ItemsSource = _searches;
        ScopesGrid.ItemsSource = _scopes;
        BuildBrowserList();
        BuildCategoryList();
        SystemGrid.ItemsSource = SystemSettingsCatalog.Pages;
        foreach (var theme in Theme.Names) ThemeBox.Items.Add(theme);
        LoadFromDraft();
        Nav.SelectedIndex = 0;
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    /// <summary>Matches the Windows title bar to the chosen theme instead of leaving it white.</summary>
    private void ApplyTitleBarTheme()
    {
        try
        {
            int dark = _draft.Appearance.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        }
        catch
        {
            // older Windows builds just keep the default title bar
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---------- load / collect ----------

    private void LoadFromDraft()
    {
        var c = _draft;

        // General
        bool combo = c.Hotkey.Mode.Equals("combo", StringComparison.OrdinalIgnoreCase);
        HotkeyModeCombo.IsChecked = combo;
        HotkeyModeDoubleTap.IsChecked = !combo;
        SelectComboItem(HotkeyKey, c.Hotkey.Key);
        HotkeyMs.Text = c.Hotkey.DoubleTapMs.ToString();
        HotkeyCombo.Text = c.Hotkey.Combo;
        SuppressInFullscreen.IsChecked = c.Hotkey.SuppressInFullscreen;
        UpdateHotkeyEnabledState();

        StartWithWindows.IsChecked = Autostart.IsEnabled;
        LearnFromUsage.IsChecked = c.LearnFromUsage;

        ThemeBox.SelectedItem = Theme.Names.Contains(c.Appearance.Theme) ? c.Appearance.Theme : "dark";
        MaxResults.Text = c.Appearance.MaxResults.ToString();
        WindowWidth.Text = c.Appearance.WindowWidth.ToString(CultureInfo.InvariantCulture);
        TopOffset.Text = c.Appearance.TopOffsetPercent.ToString(CultureInfo.InvariantCulture);
        FontScale.Text = c.Appearance.FontScale.ToString(CultureInfo.InvariantCulture);
        ShowResultShortcuts.IsChecked = c.Appearance.ShowResultShortcuts;
        ShowActionHints.IsChecked = c.Appearance.ShowActionHints;
        PlaceholderText.Text = c.Appearance.Placeholder;

        // Searches
        _searches.Clear();
        foreach (var (keyword, shortcut) in c.Searches.OrderBy(p => p.Key))
            _searches.Add(new SearchRow
            {
                Keyword = keyword,
                Name = shortcut.Name,
                Url = shortcut.Url,
                Icon = shortcut.Icon,
                ShowInHints = shortcut.ShowInHints,
            });
        DefaultSearchName.Text = c.DefaultSearchName;
        DefaultSearchUrl.Text = c.DefaultSearchUrl;

        // Files
        FilesEnabled.IsChecked = c.Files.Enabled;
        FilesInDefault.IsChecked = c.Files.InDefaultResults;
        FilesKeyword.Text = c.Files.Keyword;
        FilesRevealKeyword.Text = c.Files.RevealKeyword;
        _scopes.Clear();
        foreach (var scope in c.Files.Scopes)
            _scopes.Add(new ScopeRow { Path = scope.Path, IncludeSubfolders = scope.IncludeSubfolders });
        ExcludedFolders.Text = string.Join(Environment.NewLine, c.Files.ExcludedFolders);
        IncludedExtensions.Text = string.Join(Environment.NewLine, c.Files.IncludedExtensions);
        ExcludedExtensions.Text = string.Join(Environment.NewLine, c.Files.ExcludedExtensions);
        IndexFolders.IsChecked = c.Files.IndexFolders;
        IndexHidden.IsChecked = c.Files.IndexHidden;
        LiveWatch.IsChecked = c.Files.LiveWatch;
        FilesRefresh.Text = c.Files.RefreshMinutes.ToString();
        FilesMaxEntries.Text = c.Files.MaxEntries.ToString();
        UpdateFileStatus();

        // Bookmarks
        BookmarksEnabled.IsChecked = c.Bookmarks.Enabled;
        BookmarksInDefault.IsChecked = c.Bookmarks.InDefaultResults;
        BookmarksKeyword.Text = c.Bookmarks.Keyword;
        BookmarksRefresh.Text = c.Bookmarks.RefreshMinutes.ToString();
        BookmarksDefaultProfileOnly.IsChecked = c.Bookmarks.DefaultProfileOnly;
        foreach (var box in _browserBoxes)
            box.IsChecked = c.Bookmarks.Browsers.Any(b =>
                b.Equals((string)box.Tag, StringComparison.OrdinalIgnoreCase));
        UpdateDetectedProfiles();
        UpdateBookmarkStatus();

        // Evernote
        EvernoteEnabled.IsChecked = c.Evernote.Enabled;
        EvernoteInDefault.IsChecked = c.Evernote.InDefaultResults;
        EvernoteSearchContent.IsChecked = c.Evernote.SearchContent;
        EvernoteIncludeTrashed.IsChecked = c.Evernote.IncludeTrashed;
        EvernoteKeyword.Text = c.Evernote.Keyword;
        EvernoteRefresh.Text = c.Evernote.RefreshMinutes.ToString();
        EvernoteDbPath.Text = c.Evernote.DatabasePath;
        UpdateEvernoteStatus();

        // Windows settings
        SystemEnabled.IsChecked = c.SystemSettings.Enabled;
        SystemInDefault.IsChecked = c.SystemSettings.InDefaultResults;
        SystemKeyword.Text = c.SystemSettings.Keyword;

        // Calculator
        CalcEnabled.IsChecked = c.Calculator.Enabled;
        CalcRequireEquals.IsChecked = c.Calculator.RequireEqualsPrefix;
        CalcThousands.IsChecked = c.Calculator.ThousandsSeparator;
        CalcCopyOnEnter.IsChecked = c.Calculator.CopyOnEnter;
        CalcLargeType.IsChecked = c.Calculator.ShowLargeType;
        CalcDecimals.Text = c.Calculator.Decimals.ToString();
        CalcTest.Text = "1,250 * 3 + 15%";

        // 1Password
        OpEnabled.IsChecked = c.OnePassword.Enabled;
        OpTrigger.Text = c.OnePassword.Trigger;
        OpCliPath.Text = c.OnePassword.CliPath;
        FillExtension.IsChecked = c.OnePassword.FillMode.Equals("extension", StringComparison.OrdinalIgnoreCase);
        FillType.IsChecked = c.OnePassword.FillMode.Equals("type", StringComparison.OrdinalIgnoreCase);
        FillCopyOnly.IsChecked = c.OnePassword.FillMode.Equals("copyOnly", StringComparison.OrdinalIgnoreCase);
        if (FillExtension.IsChecked != true && FillType.IsChecked != true && FillCopyOnly.IsChecked != true)
            FillExtension.IsChecked = true;
        OpShortcut.Text = c.OnePassword.ExtensionShortcut;
        OpSubmit.IsChecked = c.OnePassword.SubmitAfterFill;
        OpPageLoad.Text = c.OnePassword.PageLoadMs.ToString();
        OpSubmitDelay.Text = c.OnePassword.SubmitDelayMs.ToString();
        OpBrowser.Text = c.OnePassword.BrowserPath;
        OpClipboardSeconds.Text = c.OnePassword.ClipboardClearSeconds.ToString();
        OpCacheMinutes.Text = c.OnePassword.CacheMinutes.ToString();
        foreach (var box in _categoryBoxes)
            box.IsChecked = c.OnePassword.CopyCategories.Any(x =>
                x.Equals((string)box.Tag, StringComparison.OrdinalIgnoreCase));

        // Advanced
        ConfigPathLabel.Text = Config.FilePath;
        UsageLabel.Text = $"{Usage.Count:N0} learned entries stored in usage.json";
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionLabel.Text = $"version {version?.ToString(3) ?? "0.2.0"}";
        AboutLabel.Text = $"Winfred {version?.ToString(3)} — an Alfred-style launcher for Windows.\n" +
                          $"Config: {Config.FilePath}";
    }

    /// <summary>Reads every control back into the draft. Returns false with a message on bad input.</summary>
    private bool CollectIntoDraft(out string error)
    {
        error = "";
        var c = _draft;

        c.Hotkey.Mode = HotkeyModeCombo.IsChecked == true ? "combo" : "doubleTap";
        c.Hotkey.Key = (HotkeyKey.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "win";
        c.Hotkey.DoubleTapMs = ParseInt(HotkeyMs.Text, c.Hotkey.DoubleTapMs, 150, 1000);
        c.Hotkey.SuppressInFullscreen = SuppressInFullscreen.IsChecked == true;
        if (c.Hotkey.Mode == "combo")
        {
            if (!KeyCombo.TryParse(HotkeyCombo.Text, out var combo) || !combo.HasModifier)
            {
                error = "The summon shortcut needs at least one modifier, e.g. Ctrl+Space.";
                return false;
            }
            c.Hotkey.Combo = combo.ToString();
        }

        c.LearnFromUsage = LearnFromUsage.IsChecked == true;

        c.Appearance.Theme = ThemeBox.SelectedItem as string ?? "dark";
        c.Appearance.MaxResults = ParseInt(MaxResults.Text, c.Appearance.MaxResults, 3, 20);
        c.Appearance.WindowWidth = ParseDouble(WindowWidth.Text, c.Appearance.WindowWidth, 480, 1400);
        c.Appearance.TopOffsetPercent = ParseDouble(TopOffset.Text, c.Appearance.TopOffsetPercent, 0, 60);
        c.Appearance.FontScale = ParseDouble(FontScale.Text, c.Appearance.FontScale, 0.8, 1.6);
        c.Appearance.ShowResultShortcuts = ShowResultShortcuts.IsChecked == true;
        c.Appearance.ShowActionHints = ShowActionHints.IsChecked == true;
        c.Appearance.Placeholder = PlaceholderText.Text;

        // Searches
        SearchesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var searches = new Dictionary<string, SearchShortcut>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _searches)
        {
            string keyword = row.Keyword.Trim();
            if (keyword.Length == 0) continue;
            if (keyword.Contains(' '))
            {
                error = $"Keyword “{keyword}” can't contain a space.";
                return false;
            }
            if (row.Url.Trim().Length == 0)
            {
                error = $"Keyword “{keyword}” needs a URL.";
                return false;
            }
            if (searches.ContainsKey(keyword))
            {
                error = $"Keyword “{keyword}” is listed twice.";
                return false;
            }
            searches[keyword] = new SearchShortcut
            {
                Name = row.Name.Trim().Length > 0 ? row.Name.Trim() : keyword,
                Url = row.Url.Trim(),
                Icon = row.Icon.Trim().Length > 0 ? row.Icon.Trim() : Glyphs.Search,
                ShowInHints = row.ShowInHints,
            };
        }
        c.Searches = searches;
        c.DefaultSearchName = DefaultSearchName.Text.Trim();
        c.DefaultSearchUrl = DefaultSearchUrl.Text.Trim();
        if (!c.DefaultSearchUrl.Contains("{q}"))
        {
            error = "The fallback search URL needs a {q} placeholder.";
            return false;
        }

        // Files
        ScopesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        c.Files.Enabled = FilesEnabled.IsChecked == true;
        c.Files.InDefaultResults = FilesInDefault.IsChecked == true;
        c.Files.Keyword = FilesKeyword.Text.Trim();
        c.Files.RevealKeyword = FilesRevealKeyword.Text.Trim();
        c.Files.Scopes = _scopes
            .Where(s => s.Path.Trim().Length > 0)
            .Select(s => new FileScope { Path = s.Path.Trim(), IncludeSubfolders = s.IncludeSubfolders })
            .ToList();
        c.Files.ExcludedFolders = SplitLines(ExcludedFolders.Text);
        c.Files.IncludedExtensions = SplitLines(IncludedExtensions.Text);
        c.Files.ExcludedExtensions = SplitLines(ExcludedExtensions.Text);
        c.Files.IndexFolders = IndexFolders.IsChecked == true;
        c.Files.IndexHidden = IndexHidden.IsChecked == true;
        c.Files.LiveWatch = LiveWatch.IsChecked == true;
        c.Files.RefreshMinutes = ParseInt(FilesRefresh.Text, c.Files.RefreshMinutes, 1, 1440);
        c.Files.MaxEntries = ParseInt(FilesMaxEntries.Text, c.Files.MaxEntries, 1000, 2_000_000);

        // Bookmarks
        c.Bookmarks.Enabled = BookmarksEnabled.IsChecked == true;
        c.Bookmarks.InDefaultResults = BookmarksInDefault.IsChecked == true;
        c.Bookmarks.Keyword = BookmarksKeyword.Text.Trim();
        c.Bookmarks.RefreshMinutes = ParseInt(BookmarksRefresh.Text, c.Bookmarks.RefreshMinutes, 1, 240);
        c.Bookmarks.DefaultProfileOnly = BookmarksDefaultProfileOnly.IsChecked == true;
        c.Bookmarks.Browsers = _browserBoxes
            .Where(b => b.IsChecked == true)
            .Select(b => (string)b.Tag)
            .ToList();

        // Evernote
        c.Evernote.Enabled = EvernoteEnabled.IsChecked == true;
        c.Evernote.InDefaultResults = EvernoteInDefault.IsChecked == true;
        c.Evernote.SearchContent = EvernoteSearchContent.IsChecked == true;
        c.Evernote.IncludeTrashed = EvernoteIncludeTrashed.IsChecked == true;
        c.Evernote.Keyword = EvernoteKeyword.Text.Trim();
        c.Evernote.RefreshMinutes = ParseInt(EvernoteRefresh.Text, c.Evernote.RefreshMinutes, 1, 1440);
        c.Evernote.DatabasePath = EvernoteDbPath.Text.Trim();

        // Windows settings
        c.SystemSettings.Enabled = SystemEnabled.IsChecked == true;
        c.SystemSettings.InDefaultResults = SystemInDefault.IsChecked == true;
        c.SystemSettings.Keyword = SystemKeyword.Text.Trim();

        // Calculator
        c.Calculator.Enabled = CalcEnabled.IsChecked == true;
        c.Calculator.RequireEqualsPrefix = CalcRequireEquals.IsChecked == true;
        c.Calculator.ThousandsSeparator = CalcThousands.IsChecked == true;
        c.Calculator.CopyOnEnter = CalcCopyOnEnter.IsChecked == true;
        c.Calculator.ShowLargeType = CalcLargeType.IsChecked == true;
        c.Calculator.Decimals = ParseInt(CalcDecimals.Text, c.Calculator.Decimals, 0, 12);

        // 1Password
        c.OnePassword.Enabled = OpEnabled.IsChecked == true;
        c.OnePassword.Trigger = OpTrigger.Text.Trim();
        c.OnePassword.CliPath = OpCliPath.Text.Trim().Length > 0 ? OpCliPath.Text.Trim() : "op";
        c.OnePassword.FillMode = FillType.IsChecked == true ? "type"
            : FillCopyOnly.IsChecked == true ? "copyOnly" : "extension";
        if (c.OnePassword.FillMode == "extension" &&
            (!KeyCombo.TryParse(OpShortcut.Text, out var fillCombo) || !fillCombo.HasModifier))
        {
            error = "The 1Password autofill shortcut needs at least one modifier, e.g. Ctrl+\\.";
            return false;
        }
        c.OnePassword.ExtensionShortcut = OpShortcut.Text.Trim();
        c.OnePassword.SubmitAfterFill = OpSubmit.IsChecked == true;
        c.OnePassword.PageLoadMs = ParseInt(OpPageLoad.Text, c.OnePassword.PageLoadMs, 300, 30_000);
        c.OnePassword.SubmitDelayMs = ParseInt(OpSubmitDelay.Text, c.OnePassword.SubmitDelayMs, 100, 10_000);
        c.OnePassword.BrowserPath = OpBrowser.Text.Trim();
        c.OnePassword.ClipboardClearSeconds = ParseInt(OpClipboardSeconds.Text, c.OnePassword.ClipboardClearSeconds, 0, 3600);
        c.OnePassword.CacheMinutes = ParseInt(OpCacheMinutes.Text, c.OnePassword.CacheMinutes, 1, 240);
        c.OnePassword.CopyCategories = _categoryBoxes
            .Where(b => b.IsChecked == true)
            .Select(b => (string)b.Tag)
            .ToList();

        var keywordClash = FindKeywordClash(c);
        if (keywordClash != null)
        {
            error = keywordClash;
            return false;
        }
        return true;
    }

    /// <summary>Two features answering to the same keyword would make one unreachable.</summary>
    private static string? FindKeywordClash(Config c)
    {
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? Claim(string keyword, string owner)
        {
            if (keyword.Length == 0) return null;
            if (used.TryGetValue(keyword, out var existing))
                return $"“{keyword}” is used by both {existing} and {owner}.";
            used[keyword] = owner;
            return null;
        }

        foreach (var keyword in c.Searches.Keys)
        {
            var clash = Claim(keyword, "a search engine");
            if (clash != null) return clash;
        }
        if (c.Files.Enabled)
        {
            var clash = Claim(c.Files.Keyword, "file search") ?? Claim(c.Files.RevealKeyword, "file reveal");
            if (clash != null) return clash;
        }
        if (c.Bookmarks.Enabled)
        {
            var clash = Claim(c.Bookmarks.Keyword, "bookmarks");
            if (clash != null) return clash;
        }
        if (c.Evernote.Enabled)
        {
            var clash = Claim(c.Evernote.Keyword, "Evernote");
            if (clash != null) return clash;
        }
        if (c.SystemSettings.Enabled)
        {
            var clash = Claim(c.SystemSettings.Keyword, "Windows settings");
            if (clash != null) return clash;
        }
        if (c.OnePassword.Enabled)
        {
            var clash = Claim(c.OnePassword.Trigger, "1Password");
            if (clash != null) return clash;
        }
        return null;
    }

    // ---------- footer ----------

    private bool ApplyDraft()
    {
        if (!CollectIntoDraft(out string error))
        {
            StatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("WinDanger");
            StatusLabel.Text = error;
            return false;
        }

        Autostart.IsEnabled = StartWithWindows.IsChecked == true;
        Config.Replace(_draft);
        _draft = Config.Current.Clone(); // keep editing a detached copy
        StatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("WinSubtext");
        StatusLabel.Text = $"Saved to {Config.FilePath} at {DateTime.Now:HH:mm:ss}";
        return true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyDraft();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ApplyDraft()) Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var panels = new UIElement[]
        {
            PanelGeneral, PanelSearches, PanelFiles, PanelBookmarks, PanelEvernote,
            PanelSystem, PanelCalculator, PanelOnePassword, PanelAdvanced,
        };
        for (int i = 0; i < panels.Length; i++)
            panels[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;

        if (Nav.SelectedIndex == 2) UpdateFileStatus();
        if (Nav.SelectedIndex == 3) UpdateBookmarkStatus();
        if (Nav.SelectedIndex == 4) UpdateEvernoteStatus();
    }

    // ---------- general ----------

    private void HotkeyMode_Changed(object sender, RoutedEventArgs e) => UpdateHotkeyEnabledState();

    private void UpdateHotkeyEnabledState()
    {
        if (HotkeyKey == null) return; // fired during InitializeComponent
        bool combo = HotkeyModeCombo.IsChecked == true;
        HotkeyKey.IsEnabled = !combo;
        HotkeyMs.IsEnabled = !combo;
        HotkeyCombo.IsEnabled = combo;
    }

    private void HotkeyCombo_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) box.Tag = box.Text; // remember in case nothing valid is pressed
    }

    private void HotkeyCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Text.Length: 0 } box && box.Tag is string previous) box.Text = previous;
    }

    private void HotkeyCombo_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) =>
        RecordCombo(HotkeyCombo, e, requireModifier: true);

    private void OpShortcut_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) =>
        RecordCombo(OpShortcut, e, requireModifier: true);

    private static void RecordCombo(TextBox target, System.Windows.Input.KeyEventArgs e, bool requireModifier)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return;

        var modifiers = Keyboard.Modifiers;
        if (requireModifier && modifiers == ModifierKeys.None)
        {
            target.Text = "press a modifier too…";
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var combo = new KeyCombo
        {
            Ctrl = (modifiers & ModifierKeys.Control) != 0,
            Alt = (modifiers & ModifierKeys.Alt) != 0,
            Shift = (modifiers & ModifierKeys.Shift) != 0,
            Win = (modifiers & ModifierKeys.Windows) != 0,
            Vk = vk,
        };
        target.Text = combo.ToString();
    }

    private void PreviewLauncher_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDraft()) return;
        (Application.Current as App)?.ShowLauncher();
    }

    // ---------- searches ----------

    private void AddSearch_Click(object sender, RoutedEventArgs e)
    {
        var row = new SearchRow { Keyword = "new", Name = "New search", Url = "https://example.com/?q={q}" };
        _searches.Add(row);
        SearchesGrid.SelectedItem = row;
        SearchesGrid.ScrollIntoView(row);
    }

    private void DuplicateSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SearchesGrid.SelectedItem is not SearchRow source) return;
        var row = new SearchRow
        {
            Keyword = source.Keyword + "2",
            Name = source.Name,
            Url = source.Url,
            Icon = source.Icon,
            ShowInHints = source.ShowInHints,
        };
        _searches.Add(row);
        SearchesGrid.SelectedItem = row;
        SearchesGrid.ScrollIntoView(row);
    }

    private void RemoveSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SearchesGrid.SelectedItem is SearchRow row) _searches.Remove(row);
    }

    private void TestSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (SearchesGrid.SelectedItem is not SearchRow row)
        {
            StatusLabel.Text = "Pick a row to test first.";
            return;
        }
        if (!row.Url.Contains("{q}"))
        {
            StatusLabel.Text = "That URL has no {q} placeholder.";
            return;
        }
        SearchEngine.OpenUrl(row.Url.Replace("{q}", Uri.EscapeDataString("winfred test")));
        StatusLabel.Text = $"Opened a test search for “{row.Name}”.";
    }

    private void MakeDefaultSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (SearchesGrid.SelectedItem is not SearchRow row) return;
        DefaultSearchName.Text = row.Name;
        DefaultSearchUrl.Text = row.Url;
        StatusLabel.Text = $"“{row.Name}” is now the fallback search.";
    }

    private void RestoreSearches_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Replace the current list with Winfred's defaults?", "Restore defaults",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _searches.Clear();
        foreach (var (keyword, shortcut) in Config.DefaultSearches().OrderBy(p => p.Key))
            _searches.Add(new SearchRow
            {
                Keyword = keyword,
                Name = shortcut.Name,
                Url = shortcut.Url,
                Icon = shortcut.Icon,
                ShowInHints = shortcut.ShowInHints,
            });
    }

    // ---------- files ----------

    private void AddScope_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Pick a folder for Winfred to index",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        if (_scopes.Any(s => string.Equals(s.Path, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            StatusLabel.Text = "That folder is already indexed.";
            return;
        }
        _scopes.Add(new ScopeRow { Path = dialog.SelectedPath, IncludeSubfolders = true });
    }

    private void RemoveScope_Click(object sender, RoutedEventArgs e)
    {
        if (ScopesGrid.SelectedItem is ScopeRow row) _scopes.Remove(row);
    }

    private void RebuildIndex_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDraft()) return;
        _engine.Files.Rebuild();
        FilesStatus.Text = "Rebuilding…";
        ScheduleStatusRefresh(UpdateFileStatus);
    }

    private void UpdateFileStatus()
    {
        var indexer = _engine.Files;
        if (indexer.IsBuilding)
        {
            FilesStatus.Text = "Rebuilding…";
            ScheduleStatusRefresh(UpdateFileStatus);
            return;
        }
        string built = indexer.BuiltAt == default
            ? "never built"
            : $"built {indexer.BuiltAt.ToLocalTime():HH:mm:ss}";
        FilesStatus.Text = indexer.BuildError != null
            ? $"Index error: {indexer.BuildError}"
            : $"{indexer.Count:N0} items indexed · {built}";
    }

    // ---------- bookmarks ----------

    private void BuildBrowserList()
    {
        var detected = BookmarkIndex.DetectSources();
        var browsers = detected.Select(s => s.Browser).Distinct().OrderBy(b => b).ToList();
        foreach (var browser in browsers.Concat(new[] { "Chrome", "Edge" }).Distinct())
        {
            int profiles = detected.Count(s => s.Browser == browser);
            var box = new CheckBox
            {
                Content = profiles > 0
                    ? $"{browser} ({profiles} profile{(profiles == 1 ? "" : "s")})"
                    : $"{browser} (not installed)",
                Tag = browser,
                IsEnabled = profiles > 0,
            };
            box.Click += BookmarkScope_Changed;
            _browserBoxes.Add(box);
            BrowserList.Items.Add(box);
        }
    }

    private void BookmarkScope_Changed(object sender, RoutedEventArgs e) => UpdateDetectedProfiles();

    /// <summary>Spells out exactly which profile files the current tick-boxes will read.</summary>
    private void UpdateDetectedProfiles()
    {
        var detected = BookmarkIndex.DetectSources();
        if (detected.Count == 0)
        {
            BookmarksDetected.Text =
                "No Chromium bookmark files found — a browser only writes one once you've saved a bookmark.";
            return;
        }

        bool defaultOnly = BookmarksDefaultProfileOnly.IsChecked == true;
        var chosen = _browserBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        var willRead = detected
            .Where(s => chosen.Any(b => b.Equals(s.Browser, StringComparison.OrdinalIgnoreCase)))
            .Where(s => !defaultOnly || BookmarkIndex.IsDefaultProfile(s.Profile))
            .ToList();
        var skipped = detected.Except(willRead).ToList();

        string reading = willRead.Count == 0
            ? "Reading: nothing — tick a browser above."
            : "Reading: " + string.Join(", ", willRead.Select(s => $"{s.Browser}/{s.Profile}"));
        string ignoring = skipped.Count == 0
            ? ""
            : "\nIgnoring: " + string.Join(", ", skipped.Select(s => $"{s.Browser}/{s.Profile}"));
        BookmarksDetected.Text = reading + ignoring;
    }

    private void ReloadBookmarks_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDraft()) return;
        _engine.Bookmarks.Refresh(force: true);
        UpdateBookmarkStatus();
    }

    private void UpdateBookmarkStatus() =>
        BookmarksStatus.Text = _engine.Bookmarks.Error != null
            ? _engine.Bookmarks.Error
            : $"{_engine.Bookmarks.Count:N0} bookmarks loaded";

    // ---------- evernote ----------

    private void DetectEvernote_Click(object sender, RoutedEventArgs e)
    {
        string? found = EvernoteIndex.DetectDatabase();
        if (found == null)
        {
            EvernoteStatus.Text = "No Evernote database found under %APPDATA%\\Evernote\\conduit-storage.";
            return;
        }
        EvernoteDbPath.Text = "";
        EvernoteStatus.Text = $"Auto-detected: {found}";
    }

    private void BrowseEvernote_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick the Evernote RemoteGraph database",
            Filter = "Evernote database (*.sql;*.db)|*.sql;*.db|All files (*.*)|*.*",
            InitialDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Evernote"),
        };
        if (dialog.ShowDialog(this) == true) EvernoteDbPath.Text = dialog.FileName;
    }

    private void ReindexEvernote_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDraft()) return;
        _engine.Evernote.EnsureLoaded(() => Dispatcher.BeginInvoke(UpdateEvernoteStatus), force: true);
        EvernoteStatus.Text = "Reading the Evernote database…";
        ScheduleStatusRefresh(UpdateEvernoteStatus);
    }

    private void UpdateEvernoteStatus()
    {
        var index = _engine.Evernote;
        if (index.IsLoading)
        {
            EvernoteStatus.Text = "Reading the Evernote database…";
            ScheduleStatusRefresh(UpdateEvernoteStatus);
            return;
        }
        string source = EvernoteIndex.ResolveDatabase();
        string where = source.Length > 0 ? source : "no database found";
        EvernoteStatus.Text = index.Error != null
            ? $"{index.Error}\n{where}"
            : $"{index.Count:N0} notes indexed\n{where}";
    }

    // ---------- windows settings ----------

    private void SystemFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SystemFilter.Text.Trim();
        SystemGrid.ItemsSource = query.Length == 0
            ? SystemSettingsCatalog.Pages
            : SystemSettingsCatalog.Search(query, 500);
    }

    // ---------- calculator ----------

    private void CalcTest_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Evaluate against the settings currently on screen, not the saved ones.
        var saved = Config.Current.Calculator;
        var probe = new CalculatorConfig
        {
            Enabled = true,
            RequireEqualsPrefix = false,
            ThousandsSeparator = CalcThousands.IsChecked == true,
            Decimals = ParseInt(CalcDecimals.Text, saved.Decimals, 0, 12),
        };
        Config.Current.Calculator = probe;
        try
        {
            var result = Calculator.TryEvaluate(CalcTest.Text);
            CalcTestResult.Text = result == null ? "—" : "= " + result.Formatted;
        }
        finally
        {
            Config.Current.Calculator = saved;
        }
    }

    // ---------- 1password ----------

    private void BuildCategoryList()
    {
        foreach (var category in KnownCategories)
        {
            var box = new CheckBox
            {
                Content = category.Replace('_', ' ').ToLowerInvariant(),
                Tag = category,
                IsEnabled = category != "LOGIN",
                ToolTip = category == "LOGIN"
                    ? "Logins are what open-and-fill exists for; use the fill mode above to change their behaviour."
                    : null,
            };
            _categoryBoxes.Add(box);
            CategoryList.Items.Add(box);
        }
    }

    private void TestOnePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDraft()) return;
        OpStatus.Text = "Running “op item list”…";
        var button = (Button)sender;
        button.IsEnabled = false;
        Task.Run(() =>
        {
            var (ok, message) = OnePasswordProvider.TestConnection();
            Dispatcher.Invoke(() =>
            {
                OpStatus.Text = message;
                OpStatus.Foreground = (System.Windows.Media.Brush)FindResource(ok ? "WinSubtext" : "WinDanger");
                button.IsEnabled = true;
            });
        });
    }

    private void BrowseOpCli_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick op.exe",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true) OpCliPath.Text = dialog.FileName;
    }

    private void BrowseBrowser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick the browser to open logins in",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) == true) OpBrowser.Text = dialog.FileName;
    }

    // ---------- advanced ----------

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDraft()) return;
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Config.FilePath}\"") { UseShellExecute = true });
    }

    private void ShowConfigFolder_Click(object sender, RoutedEventArgs e) =>
        SearchEngine.Reveal(Config.FilePath);

    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDraft()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Winfred settings",
            FileName = "winfred-config.json",
            Filter = "JSON (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.Copy(Config.FilePath, dialog.FileName, overwrite: true);
            StatusLabel.Text = $"Exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Export failed: {ex.Message}";
        }
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Winfred settings",
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.Copy(dialog.FileName, Config.FilePath, overwrite: true);
            Config.Load();
            _draft = Config.Current.Clone();
            LoadFromDraft();
            StatusLabel.Text = $"Imported {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Import failed: {ex.Message}";
        }
    }

    private void ResetUsage_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Forget which results you've picked before?", "Reset learning",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        Usage.Reset();
        UsageLabel.Text = "0 learned entries stored in usage.json";
        StatusLabel.Text = "Learning reset.";
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Restore every Winfred setting to its default?\nThis cannot be undone.",
                "Restore defaults", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        _draft = new Config();
        LoadFromDraft();
        StatusLabel.Text = "Defaults loaded — press Apply or Save to keep them.";
    }

    // ---------- helpers ----------

    private void ScheduleStatusRefresh(Action refresh)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            refresh();
        };
        timer.Start();
    }

    private static void SelectComboItem(ComboBox box, string value)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private static List<string> SplitLines(string text) => text
        .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => x.Length > 0)
        .ToList();

    private static int ParseInt(string text, int fallback, int min, int max) =>
        int.TryParse(text.Trim(), out int value) ? Math.Clamp(value, min, max) : fallback;

    private static double ParseDouble(string text, double fallback, double min, double max) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? Math.Clamp(value, min, max)
            : fallback;
}
