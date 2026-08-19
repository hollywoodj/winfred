using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Winfred;

/// <summary>
/// Keeps an in-memory index of the folders the user picked in Settings, persisted to disk
/// so a restart is instant, refreshed on a timer and kept live by FileSystemWatchers.
/// </summary>
public sealed class FileIndexer : IDisposable
{
    private const int FileFormatVersion = 1;

    public sealed record Entry(string Path, string Name, string NameLower, long Size, DateTime Modified, bool IsDirectory);

    private readonly object _lock = new();
    private List<Entry> _entries = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, byte> _pendingRemovals = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _refreshTimer;
    private System.Threading.Timer? _saveTimer;
    private volatile bool _building;
    private DateTime _builtAt;
    private string? _buildError;

    public int Count { get { lock (_lock) return _entries.Count; } }
    public bool IsBuilding => _building;
    public DateTime BuiltAt => _builtAt;
    public string? BuildError => _buildError;

    /// <summary>Raised (off the UI thread) when the index contents changed.</summary>
    public event Action? IndexChanged;

    private static string IndexPath => System.IO.Path.Combine(Config.Directory, "file-index.bin");

    public void Start()
    {
        LoadFromDisk();
        Reconfigure();
    }

    /// <summary>Re-reads config: rewires watchers, reschedules refreshes, rebuilds if stale.</summary>
    public void Reconfigure()
    {
        StopWatchers();
        var cfg = Config.Current.Files;
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        if (!cfg.Enabled)
        {
            lock (_lock) _entries = new List<Entry>();
            return;
        }

        if (cfg.LiveWatch) StartWatchers(cfg);

        int minutes = Math.Clamp(cfg.RefreshMinutes, 1, 24 * 60);
        _refreshTimer = new System.Threading.Timer(_ => Rebuild(), null,
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));

        bool stale;
        lock (_lock) stale = _entries.Count == 0 || DateTime.UtcNow - _builtAt > TimeSpan.FromMinutes(minutes);
        if (stale) Rebuild();
    }

    public void Rebuild()
    {
        if (_building) return;
        _building = true;
        IndexChanged?.Invoke();
        Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var cfg = Config.Current.Files;
                var collected = new List<Entry>(Math.Min(cfg.MaxEntries, 50_000));
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var scope in cfg.Scopes)
                {
                    if (string.IsNullOrWhiteSpace(scope.Path) || !Directory.Exists(scope.Path)) continue;
                    Walk(scope.Path, scope.IncludeSubfolders, cfg, collected, seen);
                    if (collected.Count >= cfg.MaxEntries) break;
                }
                lock (_lock) _entries = collected;
                _builtAt = DateTime.UtcNow;
                _buildError = null;
                Debug.WriteLine($"Winfred: indexed {collected.Count} entries in {stopwatch.ElapsedMilliseconds}ms");
                ScheduleSave();
            }
            catch (Exception ex)
            {
                _buildError = ex.Message;
            }
            finally
            {
                _building = false;
                IndexChanged?.Invoke();
            }
        });
    }

    private static void Walk(string root, bool recurse, FileSearchConfig cfg, List<Entry> output, HashSet<string> seen)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0 && output.Count < cfg.MaxEntries)
        {
            string dir = queue.Dequeue();
            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(dir).EnumerateFileSystemInfos();
            }
            catch (Exception)
            {
                continue; // unreadable folder (permissions, offline OneDrive, junction loop)
            }

            foreach (var child in children)
            {
                if (output.Count >= cfg.MaxEntries) return;

                bool hidden = (child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                if (hidden && !cfg.IndexHidden) continue;
                bool isDir = (child.Attributes & FileAttributes.Directory) != 0;

                if (isDir)
                {
                    if (IsExcludedFolder(child.Name, cfg)) continue;
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0) continue; // don't follow junctions
                    if (cfg.IndexFolders && seen.Add(child.FullName))
                        output.Add(Make(child, true));
                    if (recurse) queue.Enqueue(child.FullName);
                }
                else
                {
                    if (!IsIncludedFile(child.Name, cfg)) continue;
                    if (seen.Add(child.FullName))
                        output.Add(Make(child, false));
                }
            }
        }
    }

    private static Entry Make(FileSystemInfo info, bool isDirectory)
    {
        long size = 0;
        try { size = isDirectory ? 0 : ((FileInfo)info).Length; } catch { /* vanished mid-scan */ }
        DateTime modified;
        try { modified = info.LastWriteTimeUtc; } catch { modified = DateTime.MinValue; }
        return new Entry(info.FullName, info.Name, info.Name.ToLowerInvariant(), size, modified, isDirectory);
    }

    private static bool IsExcludedFolder(string name, FileSearchConfig cfg) =>
        cfg.ExcludedFolders.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsIncludedFile(string name, FileSearchConfig cfg)
    {
        string ext = System.IO.Path.GetExtension(name);
        if (cfg.IncludedExtensions.Count > 0)
            return cfg.IncludedExtensions.Any(x => Matches(x, ext));
        return !cfg.ExcludedExtensions.Any(x => Matches(x, ext));

        static bool Matches(string configured, string ext)
        {
            var normalized = configured.StartsWith('.') ? configured : "." + configured;
            return string.Equals(normalized, ext, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- search ----------

    public List<Entry> Search(string query, int limit, bool foldersOnly = false)
    {
        List<Entry> snapshot;
        lock (_lock) snapshot = _entries;
        if (snapshot.Count == 0) return new List<Entry>();

        string trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return snapshot
                .Where(e => !foldersOnly || e.IsDirectory)
                .OrderByDescending(e => e.Modified)
                .Take(limit)
                .ToList();
        }

        var terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        IEnumerable<Entry> source = snapshot;
        if (foldersOnly) source = source.Where(e => e.IsDirectory);

        var scored = (snapshot.Count > 20_000 ? source.AsParallel() : source)
            .Select(entry => (entry, score: ScoreEntry(entry, terms)))
            .Where(x => x.score > 0)
            .ToList();

        return scored
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.entry.Modified)
            .Take(limit)
            .Select(x => x.entry)
            .ToList();
    }

    private static double ScoreEntry(Entry entry, string[] terms)
    {
        double total = 0;
        foreach (var term in terms)
        {
            // Cheap gate first: most entries fail here, so the fuzzy walk stays rare.
            double score;
            if (ContainsSubsequence(entry.NameLower, term))
                score = FuzzyMatcher.Score(entry.Name, term);
            else if (entry.Path.Contains(term, StringComparison.OrdinalIgnoreCase))
                score = 20; // matched only via the folder path
            else
                return 0;

            if (score <= 0) return 0;
            total += score;
        }

        double average = total / terms.Length;
        if (entry.IsDirectory) average += 4; // folders are usually what you want when both match
        double ageDays = (DateTime.UtcNow - entry.Modified).TotalDays;
        average += 10.0 / (1.0 + Math.Max(0, ageDays) / 30.0); // nudge recently touched files up
        return average;
    }

    private static bool ContainsSubsequence(string text, string query)
    {
        int index = 0;
        foreach (char c in query)
        {
            index = text.IndexOf(c, index);
            if (index < 0) return false;
            index++;
        }
        return true;
    }

    // ---------- live updates ----------

    private void StartWatchers(FileSearchConfig cfg)
    {
        foreach (var scope in cfg.Scopes)
        {
            if (string.IsNullOrWhiteSpace(scope.Path) || !Directory.Exists(scope.Path)) continue;
            try
            {
                var watcher = new FileSystemWatcher(scope.Path)
                {
                    IncludeSubdirectories = scope.IncludeSubfolders,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) => OnCreated(e.FullPath);
                watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
                watcher.Renamed += (_, e) => { OnDeleted(e.OldFullPath); OnCreated(e.FullPath); };
                watcher.Error += (_, _) => Rebuild(); // buffer overflow — cheaper to start over
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception)
            {
                // a scope we can't watch still gets picked up by the timed rebuild
            }
        }
    }

    private void StopWatchers()
    {
        foreach (var watcher in _watchers)
        {
            try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { /* already gone */ }
        }
        _watchers.Clear();
    }

    private void OnCreated(string path)
    {
        try
        {
            var cfg = Config.Current.Files;
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists) return;
            bool isDir = info is DirectoryInfo;
            if (isDir && (!cfg.IndexFolders || IsExcludedFolder(info.Name, cfg))) return;
            if (!isDir && !IsIncludedFile(info.Name, cfg)) return;
            if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 && !cfg.IndexHidden) return;

            lock (_lock)
            {
                if (_entries.Count >= cfg.MaxEntries) return;
                if (_entries.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) return;
                _entries = new List<Entry>(_entries) { Make(info, isDir) };
            }
            ScheduleSave();
        }
        catch
        {
            // transient file that disappeared again before we could stat it
        }
    }

    private void OnDeleted(string path)
    {
        lock (_lock)
        {
            int index = _entries.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var copy = new List<Entry>(_entries);
            copy.RemoveAt(index);
            _entries = copy;
        }
        ScheduleSave();
    }

    // ---------- persistence ----------

    private void ScheduleSave()
    {
        _saveTimer ??= new System.Threading.Timer(_ => SaveToDisk(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer.Change(5000, Timeout.Infinite);
    }

    public void SaveToDisk()
    {
        try
        {
            List<Entry> snapshot;
            lock (_lock) snapshot = _entries;
            Directory.CreateDirectory(Config.Directory);
            using var stream = File.Create(IndexPath);
            using var writer = new BinaryWriter(stream);
            writer.Write(FileFormatVersion);
            writer.Write(_builtAt.Ticks);
            writer.Write(snapshot.Count);
            foreach (var entry in snapshot)
            {
                writer.Write(entry.Path);
                writer.Write(entry.Size);
                writer.Write(entry.Modified.Ticks);
                writer.Write(entry.IsDirectory);
            }
        }
        catch
        {
            // the index rebuilds itself on next launch if it can't be cached
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(IndexPath)) return;
            using var stream = File.OpenRead(IndexPath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != FileFormatVersion) return;
            _builtAt = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            int count = reader.ReadInt32();
            var loaded = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                string path = reader.ReadString();
                long size = reader.ReadInt64();
                var modified = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                bool isDir = reader.ReadBoolean();
                string name = System.IO.Path.GetFileName(path);
                if (name.Length == 0) name = path;
                loaded.Add(new Entry(path, name, name.ToLowerInvariant(), size, modified, isDir));
            }
            lock (_lock) _entries = loaded;
        }
        catch
        {
            lock (_lock) _entries = new List<Entry>();
        }
    }

    public void Dispose()
    {
        StopWatchers();
        _refreshTimer?.Dispose();
        _saveTimer?.Dispose();
        SaveToDisk();
    }
}
