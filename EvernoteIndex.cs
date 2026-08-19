using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Winfred;

/// <summary>
/// Indexes Evernote v10 notes by reading its local "conduit" SQLite graph. The live file
/// is locked while Evernote runs, so it is snapshotted to a temp copy before each read.
/// Titles, notebooks, tags and the offline note text are all searchable.
/// </summary>
public sealed class EvernoteIndex
{
    public sealed record Note(
        string Id,
        string Title,
        string Snippet,
        string Content,
        string Notebook,
        string Tags,
        DateTime Updated,
        bool Trashed,
        long Owner,
        string Shard)
    {
        /// <summary>Opens the note in the Evernote desktop app.</summary>
        public string AppLink => $"evernote:///view/{Owner}/{Shard}/{Id}/{Id}/";
        public string WebLink => $"https://www.evernote.com/client/web#?n={Id}&";
    }

    private readonly object _lock = new();
    private List<Note> _notes = new();
    private DateTime _loadedAt;
    private DateTime _sourceStamp;
    private bool _loading;
    private string? _error;

    public int Count { get { lock (_lock) return _notes.Count; } }
    public string? Error => _error;
    public DateTime LoadedAt => _loadedAt;
    public bool IsLoading => _loading;

    private static string SnapshotPath => Path.Combine(Config.Directory, "evernote-snapshot.db");

    /// <summary>Finds the biggest RemoteGraph database under the Evernote conduit storage.</summary>
    public static string? DetectDatabase()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Evernote", "conduit-storage");
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, "*RemoteGraph.sql", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    public static string ResolveDatabase()
    {
        var configured = Config.Current.Evernote.DatabasePath;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return DetectDatabase() ?? "";
    }

    /// <param name="requery">Invoked when a background load finishes and results should refresh.</param>
    public void EnsureLoaded(Action? requery = null, bool force = false)
    {
        var cfg = Config.Current.Evernote;
        if (!cfg.Enabled || _loading) return;

        var interval = TimeSpan.FromMinutes(Math.Clamp(cfg.RefreshMinutes, 1, 24 * 60));
        bool due = force || _loadedAt == default || DateTime.UtcNow - _loadedAt > interval;
        if (!due) return;

        string source = ResolveDatabase();
        if (source.Length == 0)
        {
            _error = "Evernote database not found — is the Evernote desktop app installed and signed in?";
            _loadedAt = DateTime.UtcNow;
            return;
        }

        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(source); }
        catch { stamp = DateTime.UtcNow; }
        if (!force && stamp == _sourceStamp && _notes.Count > 0)
        {
            _loadedAt = DateTime.UtcNow; // unchanged on disk; nothing to re-read
            return;
        }

        _loading = true;
        Task.Run(() =>
        {
            try
            {
                Load(source);
                _sourceStamp = stamp;
                _error = null;
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
            finally
            {
                _loadedAt = DateTime.UtcNow;
                _loading = false;
                requery?.Invoke();
            }
        });
    }

    private void Load(string source)
    {
        Directory.CreateDirectory(Config.Directory);
        // Evernote holds an exclusive lock, so read from a copy. -wal/-shm are copied when present.
        File.Copy(source, SnapshotPath, overwrite: true);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(source + suffix)) File.Copy(source + suffix, SnapshotPath + suffix, overwrite: true);
                else if (File.Exists(SnapshotPath + suffix)) File.Delete(SnapshotPath + suffix);
            }
            catch { /* the main file alone is enough to read committed notes */ }
        }

        var notebooks = new Dictionary<string, string>();
        var tags = new Dictionary<string, List<string>>();
        var content = new Dictionary<string, string>();
        var notes = new List<Note>();

        using var connection = new SqliteConnection($"Data Source={SnapshotPath};Mode=ReadOnly;Cache=Private");
        connection.Open();

        Read(connection, "SELECT id, label FROM Nodes_Notebook", reader =>
            notebooks[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1));

        Read(connection,
            "SELECT nt.Note_id, t.label FROM NoteTag nt JOIN Nodes_Tag t ON t.id = nt.Tag_id",
            reader =>
            {
                string noteId = reader.GetString(0);
                if (!tags.TryGetValue(noteId, out var list)) tags[noteId] = list = new List<string>();
                if (!reader.IsDBNull(1)) list.Add(reader.GetString(1));
            });

        if (Config.Current.Evernote.SearchContent)
        {
            Read(connection, "SELECT id, content FROM Offline_Search_Note_Content", reader =>
                content[reader.GetString(0)] = reader.IsDBNull(1) ? "" : CleanText(reader.GetString(1)));
        }

        Read(connection,
            @"SELECT id, label, snippet, updated, deleted, parent_Notebook_id, owner, shardId
              FROM Nodes_Note",
            reader =>
            {
                string id = reader.GetString(0);
                string title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (title.Length == 0) title = "(untitled)";
                string snippet = reader.IsDBNull(2) ? "" : CleanText(reader.GetString(2));
                var updated = reader.IsDBNull(3) ? DateTime.MinValue : FromUnixMs(reader.GetInt64(3));
                bool trashed = !reader.IsDBNull(4);
                string notebookId = reader.IsDBNull(5) ? "" : reader.GetString(5);
                long owner = reader.IsDBNull(6) ? 0 : (long)reader.GetDouble(6);
                string shard = reader.IsDBNull(7) ? "" : reader.GetString(7);

                notes.Add(new Note(
                    id,
                    title,
                    snippet,
                    content.TryGetValue(id, out var body) ? body : "",
                    notebooks.TryGetValue(notebookId, out var notebook) ? notebook : "",
                    tags.TryGetValue(id, out var noteTags) ? string.Join(", ", noteTags) : "",
                    updated,
                    trashed,
                    owner,
                    shard));
            });

        lock (_lock) _notes = notes;
    }

    private static void Read(SqliteConnection connection, string sql, Action<SqliteDataReader> handle)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read()) handle(reader);
        }
        catch (SqliteException)
        {
            // Evernote occasionally renames tables between versions; a missing one is not fatal
        }
    }

    private static DateTime FromUnixMs(long milliseconds) =>
        milliseconds <= 0 ? DateTime.MinValue : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;

    /// <summary>Evernote stores note text with literal "/n" line markers and heavy padding.</summary>
    private static string CleanText(string raw)
    {
        var text = raw.Replace("/n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();
        return text.Length > 4000 ? text[..4000] : text;
    }

    public List<Note> Search(string query, int limit, Action? requery = null)
    {
        EnsureLoaded(requery);
        List<Note> snapshot;
        lock (_lock) snapshot = _notes;

        bool includeTrashed = Config.Current.Evernote.IncludeTrashed;
        IEnumerable<Note> pool = includeTrashed ? snapshot : snapshot.Where(n => !n.Trashed);

        string trimmed = query.Trim();
        if (trimmed.Length == 0)
            return pool.OrderByDescending(n => n.Updated).Take(limit).ToList();

        var terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool searchContent = Config.Current.Evernote.SearchContent && trimmed.Length >= 3;

        return pool
            .Select(n => (n, score: ScoreNote(n, terms, searchContent)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.n.Updated)
            .Take(limit)
            .Select(x => x.n)
            .ToList();
    }

    private static double ScoreNote(Note note, string[] terms, bool searchContent)
    {
        double total = 0;
        foreach (var term in terms)
        {
            double score = FuzzyMatcher.Score(note.Title, term);
            if (score <= 0 && note.Tags.Contains(term, StringComparison.OrdinalIgnoreCase)) score = 40;
            if (score <= 0 && note.Notebook.Contains(term, StringComparison.OrdinalIgnoreCase)) score = 25;
            if (score <= 0 && searchContent)
            {
                if (note.Content.Contains(term, StringComparison.OrdinalIgnoreCase)) score = 18;
                else if (note.Snippet.Contains(term, StringComparison.OrdinalIgnoreCase)) score = 14;
            }
            if (score <= 0) return 0;
            total += score;
        }

        double average = total / terms.Length;
        double ageDays = (DateTime.UtcNow - note.Updated).TotalDays;
        average += 8.0 / (1.0 + Math.Max(0, ageDays) / 60.0);
        if (note.Trashed) average -= 30;
        return average;
    }

    /// <summary>Short "Notebook · tags · updated" line for the result subtitle.</summary>
    public static string Describe(Note note)
    {
        var parts = new StringBuilder();
        if (note.Notebook.Length > 0) parts.Append(note.Notebook);
        if (note.Tags.Length > 0) parts.Append(parts.Length > 0 ? " · " : "").Append('#').Append(note.Tags);
        if (note.Updated > DateTime.MinValue)
            parts.Append(parts.Length > 0 ? " · " : "").Append(note.Updated.ToLocalTime().ToString("d MMM yyyy"));
        if (note.Trashed) parts.Append(" · in trash");
        if (note.Snippet.Length > 0)
            parts.Append(parts.Length > 0 ? " — " : "").Append(Truncate(note.Snippet, 90));
        return parts.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
