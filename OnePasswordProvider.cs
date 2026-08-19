using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winfred;

/// <summary>
/// Lists 1Password items via the `op` CLI. Logins open their website and get filled
/// (Alfred's "open and fill"); passwords, API credentials and other secrets are copied.
/// Requires the 1Password desktop app with Settings → Developer → "Integrate with
/// 1Password CLI" enabled, so `op` can authenticate through the app (biometrics/PIN).
/// </summary>
public class OnePasswordProvider
{
    private const string SetupUrl = "https://developer.1password.com/docs/cli/get-started/";

    private List<OpItem>? _items;
    private DateTime _fetchedAt;
    private bool _fetching;
    private string? _error;
    private bool _cliMissing;

    public List<ResultItem> GetResults(string filter, Action requery)
    {
        var cfg = Config.Current.OnePassword;
        var ttl = TimeSpan.FromMinutes(Math.Clamp(cfg.CacheMinutes, 1, 240));
        bool stale = _items == null || DateTime.UtcNow - _fetchedAt > ttl;
        if (stale && !_fetching && _error == null && !_cliMissing)
            BeginFetch(requery);

        if (_cliMissing)
        {
            return new List<ResultItem>
            {
                new()
                {
                    Icon = "🔐",
                    Title = "1Password CLI (op) is not installed",
                    Subtitle = "Install with “winget install AgileBits.1Password.CLI”, then enable " +
                               "Settings → Developer → Integrate with 1Password CLI in the app",
                    Kind = ResultKind.Error,
                    Enter = ResultAction.Of("setup guide", () => SearchEngine.OpenUrl(SetupUrl)),
                },
                RetryItem(requery),
            };
        }

        if (_error != null)
        {
            return new List<ResultItem>
            {
                ResultItem.Info("⚠️", "1Password CLI error", _error, ResultKind.Error),
                RetryItem(requery),
            };
        }

        if (_items == null)
        {
            return new List<ResultItem>
            {
                ResultItem.Info("⏳", "Loading 1Password items…",
                    "Running “op item list” — approve the prompt in 1Password if one appears"),
            };
        }

        var query = filter.Trim();
        int limit = Config.Current.Appearance.MaxResults;
        var matches = _items
            .Select(item => (item, score: Score(item, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score + Usage.Boost(query, Uid(x.item)))
            .ThenBy(x => x.item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => ToResult(x.item, x.score))
            .ToList();

        if (matches.Count == 0)
        {
            matches.Add(ResultItem.Info("🔐", $"No 1Password items match “{query}”",
                $"{_items.Count} items in cache · Ctrl+R style refresh: type {Config.Current.OnePassword.Trigger} and pick Retry"));
        }
        return matches;
    }

    private static double Score(OpItem item, string query)
    {
        if (query.Length == 0) return 1;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double total = 0;
        foreach (var term in terms)
        {
            double score = FuzzyMatcher.Score(item.Title, term);
            if (score <= 0 && item.Username != null) score = FuzzyMatcher.Score(item.Username, term) * 0.8;
            if (score <= 0 && item.PrimaryUrl != null)
                score = FuzzyMatcher.Score(BookmarkIndex.HostOf(item.PrimaryUrl), term) * 0.8;
            if (score <= 0 && item.VaultName.Contains(term, StringComparison.OrdinalIgnoreCase)) score = 10;
            if (score <= 0) return 0;
            total += score;
        }
        return total / terms.Length;
    }

    private static string Uid(OpItem item) => "1p:" + item.Id;

    private ResultItem RetryItem(Action requery) => new()
    {
        Icon = "🔄",
        Title = "Retry",
        Subtitle = "Re-run “op item list”",
        Enter = ResultAction.Of("retry", () =>
        {
            _error = null;
            _cliMissing = false;
            _items = null;
            requery();
        }, hidesWindow: false),
    };

    private ResultItem ToResult(OpItem item, double score)
    {
        var cfg = Config.Current.OnePassword;
        bool copyOnly = cfg.IsCopyCategory(item.Category) || item.PrimaryUrl == null;
        string where = item.Username ?? item.PrimaryUrl ?? item.Category.ToLowerInvariant().Replace('_', ' ');

        if (copyOnly)
        {
            string label = SecretLabel(item.Category);
            return new ResultItem
            {
                Icon = IconFor(item.Category),
                Title = item.Title,
                Subtitle = $"{where} · {item.VaultName} — Enter copies the {label}",
                Kind = ResultKind.Credential,
                Uid = Uid(item),
                Score = score,
                Enter = ResultAction.Of($"copy {label}", () => CopySecret(item)),
                CtrlEnter = ResultAction.Of("copy username", () => CopyField(item, "username")),
                ShiftEnter = item.PrimaryUrl != null
                    ? ResultAction.Of("open site", () => AutoFill.OpenOnly(item.PrimaryUrl))
                    : null,
                AltEnter = ResultAction.Of("open in 1Password", () => OpenInApp(item)),
            };
        }

        string fillHint = cfg.FillMode.Equals("type", StringComparison.OrdinalIgnoreCase)
            ? "opens the site and types your login"
            : "opens the site and triggers 1Password autofill";

        return new ResultItem
        {
            Icon = IconFor(item.Category),
            Title = item.Title,
            Subtitle = $"{where} · {item.VaultName} — Enter {fillHint}",
            Kind = ResultKind.Credential,
            Uid = Uid(item),
            Score = score,
            Enter = ResultAction.Of("open & fill", () => OpenAndFill(item)),
            CtrlEnter = ResultAction.Of("copy password", () => CopyField(item, "password")),
            ShiftEnter = ResultAction.Of("copy username", () => CopyField(item, "username")),
            AltEnter = ResultAction.Of("open site only", () => AutoFill.OpenOnly(item.PrimaryUrl!)),
        };
    }

    private static string SecretLabel(string category) => category.ToUpperInvariant() switch
    {
        "API_CREDENTIAL" => "API key",
        "SECURE_NOTE" => "note",
        "CREDIT_CARD" => "card number",
        "SSH_KEY" => "private key",
        _ => "password",
    };

    private static string IconFor(string category) => category.ToUpperInvariant() switch
    {
        "LOGIN" => "🔐",
        "PASSWORD" => "🔑",
        "API_CREDENTIAL" => "🗝️",
        "SECURE_NOTE" => "📝",
        "CREDIT_CARD" => "💳",
        "IDENTITY" => "🪪",
        "SSH_KEY" => "🔏",
        "DATABASE" => "🗄️",
        "SERVER" => "🖥️",
        "WIRELESS_ROUTER" => "📶",
        "SOFTWARE_LICENSE" => "📜",
        _ => "🔐",
    };

    private static void OpenInApp(OpItem item) =>
        SearchEngine.OpenUrl($"onepassword://view-item/?a={item.AccountId}&v={item.VaultId}&i={item.Id}");

    // ---------- actions ----------

    private static void OpenAndFill(OpItem item)
    {
        Task.Run(() =>
        {
            try
            {
                var detail = FetchDetail(item.Id);
                string? url = detail.Url ?? item.PrimaryUrl;
                if (url == null)
                {
                    Notifier.Notify($"“{item.Title}” has no website saved — copying the password instead.");
                    CopyValue(detail.Password, item.Title, "password");
                    return;
                }
                AutoFill.OpenAndFill(new AutoFill.FillRequest(item.Title, url, detail.Username, detail.Password));
            }
            catch (Exception ex)
            {
                Notifier.Notify($"1Password: {FirstLine(ex.Message)}");
            }
        });
    }

    /// <summary>Copies whatever the item's primary secret is — password, API key, note…</summary>
    private static void CopySecret(OpItem item)
    {
        Task.Run(() =>
        {
            try
            {
                var detail = FetchDetail(item.Id);
                string? value = detail.Password ?? detail.FirstConcealed ?? detail.NoteText;
                CopyValue(value, item.Title, SecretLabel(item.Category));
            }
            catch (Exception ex)
            {
                Notifier.Notify($"1Password: {FirstLine(ex.Message)}");
            }
        });
    }

    private static void CopyField(OpItem item, string field)
    {
        Task.Run(() =>
        {
            try
            {
                string value = RunOp($"item get {item.Id} --fields {field} --reveal").Trim();
                CopyValue(value, item.Title, field);
            }
            catch (Exception ex)
            {
                Notifier.Notify($"1Password: {FirstLine(ex.Message)}");
            }
        });
    }

    private static void CopyValue(string? value, string title, string field)
    {
        if (string.IsNullOrEmpty(value))
        {
            Notifier.Notify($"“{title}” has no {field} field.");
            return;
        }
        int clearAfter = Config.Current.OnePassword.ClipboardClearSeconds;
        System.Windows.Application.Current.Dispatcher.Invoke(() => ClipboardGuard.Copy(value, clearAfter));
        Notifier.Notify(clearAfter > 0
            ? $"{Capitalize(field)} for “{title}” copied — clipboard clears in {clearAfter}s."
            : $"{Capitalize(field)} for “{title}” copied.");
    }

    // ---------- op CLI ----------

    private void BeginFetch(Action requery)
    {
        _fetching = true;
        Task.Run(() =>
        {
            try
            {
                string json = RunOp("item list --format json");
                var parsed = JsonSerializer.Deserialize<List<OpItemJson>>(json) ?? new List<OpItemJson>();
                _items = parsed
                    .Select(p => new OpItem(
                        p.Id ?? "",
                        p.Title ?? "(untitled)",
                        string.IsNullOrWhiteSpace(p.AdditionalInformation) ? null : p.AdditionalInformation,
                        p.Vault?.Name ?? "",
                        p.Vault?.Id ?? "",
                        p.Category ?? "LOGIN",
                        p.Urls?.FirstOrDefault(u => u.Primary)?.Href ?? p.Urls?.FirstOrDefault()?.Href,
                        ""))
                    .Where(i => i.Id.Length > 0)
                    .ToList();
                _fetchedAt = DateTime.UtcNow;
                _error = null;
            }
            catch (OpNotFoundException)
            {
                _cliMissing = true;
            }
            catch (Exception ex)
            {
                _error = FirstLine(ex.Message);
            }
            finally
            {
                _fetching = false;
                requery();
            }
        });
    }

    private static OpDetail FetchDetail(string id)
    {
        string json = RunOp($"item get {id} --format json --reveal");
        var item = JsonSerializer.Deserialize<OpItemDetailJson>(json)
                   ?? throw new Exception("could not read the item");

        string? Field(Func<OpFieldJson, bool> predicate) =>
            item.Fields?.FirstOrDefault(predicate)?.Value;

        string? username = Field(f => string.Equals(f.Purpose, "USERNAME", StringComparison.OrdinalIgnoreCase))
                           ?? Field(f => string.Equals(f.Label, "username", StringComparison.OrdinalIgnoreCase));
        string? password = Field(f => string.Equals(f.Purpose, "PASSWORD", StringComparison.OrdinalIgnoreCase))
                           ?? Field(f => string.Equals(f.Label, "password", StringComparison.OrdinalIgnoreCase))
                           ?? Field(f => string.Equals(f.Label, "credential", StringComparison.OrdinalIgnoreCase));
        string? firstConcealed = Field(f =>
            string.Equals(f.Type, "CONCEALED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(f.Value));
        string? note = Field(f => string.Equals(f.Purpose, "NOTES", StringComparison.OrdinalIgnoreCase));
        string? url = item.Urls?.FirstOrDefault(u => u.Primary)?.Href ?? item.Urls?.FirstOrDefault()?.Href;

        return new OpDetail(username, password, firstConcealed, note, url);
    }

    private static string RunOp(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Config.Current.OnePassword.CliPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new OpNotFoundException();
        }

        using (process)
        {
            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(); } catch { /* already gone */ }
                throw new Exception("op timed out — is 1Password unlocked?");
            }
            string stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                throw new Exception(stderr.Length > 0 ? stderr : $"op exited with code {process.ExitCode}");
            return stdout;
        }
    }

    /// <summary>Quick check used by the settings window's "Test connection" button.</summary>
    public static (bool Ok, string Message) TestConnection()
    {
        try
        {
            string json = RunOp("item list --format json");
            int count = JsonSerializer.Deserialize<List<OpItemJson>>(json)?.Count ?? 0;
            return (true, $"Connected — {count} items visible to the CLI.");
        }
        catch (OpNotFoundException)
        {
            return (false, "op CLI not found. Install it, or set the full path in Settings → 1Password.");
        }
        catch (Exception ex)
        {
            return (false, FirstLine(ex.Message));
        }
    }

    private static string FirstLine(string s)
    {
        var line = s.Split('\n')[0].Trim();
        // op prefixes errors like "[ERROR] 2026/07/16 10:00:00 message" — keep just the message
        if (line.StartsWith("[ERROR]"))
        {
            var parts = line.Split(' ', 4);
            if (parts.Length == 4) line = parts[3];
        }
        return line;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---------- op JSON shapes ----------

    private sealed class OpItemJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("additional_information")] public string? AdditionalInformation { get; set; }
        [JsonPropertyName("vault")] public OpVaultJson? Vault { get; set; }
        [JsonPropertyName("urls")] public List<OpUrlJson>? Urls { get; set; }
    }

    private sealed class OpItemDetailJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("fields")] public List<OpFieldJson>? Fields { get; set; }
        [JsonPropertyName("urls")] public List<OpUrlJson>? Urls { get; set; }
    }

    private sealed class OpFieldJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("purpose")] public string? Purpose { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    private sealed class OpVaultJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class OpUrlJson
    {
        [JsonPropertyName("primary")] public bool Primary { get; set; }
        [JsonPropertyName("href")] public string? Href { get; set; }
    }

    private sealed record OpItem(
        string Id, string Title, string? Username, string VaultName, string VaultId,
        string Category, string? PrimaryUrl, string AccountId);

    private sealed record OpDetail(
        string? Username, string? Password, string? FirstConcealed, string? NoteText, string? Url);

    private sealed class OpNotFoundException : Exception;
}
