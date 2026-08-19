using System.Diagnostics;
using System.IO;

namespace Winfred;

/// <summary>Indexes applications exposed by the Windows Start menu, including Store apps.</summary>
public sealed class ApplicationIndex
{
    public sealed record Entry(string Name, string Target, bool IsAppId, string Location);

    private readonly object _lock = new();
    private List<Entry> _entries = new();

    public event Action? Changed;

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public void Start()
    {
        var shortcuts = ReadStartMenuShortcuts();
        Replace(shortcuts);
        _ = Task.Run(() => AddStartApps(shortcuts));
    }

    public List<Entry> Search(string query, int limit)
    {
        if (query.Length == 0 || limit <= 0) return new();

        List<Entry> snapshot;
        lock (_lock) snapshot = _entries.ToList();

        return snapshot
            .Select(entry => (Entry: entry, Score: FuzzyMatcher.Score(entry.Name, query)))
            .Where(match => match.Score > FuzzyMatcher.NoMatch)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .Select(match => match.Entry)
            .ToList();
    }

    public static void Launch(Entry entry)
    {
        if (!entry.IsAppId)
        {
            Process.Start(new ProcessStartInfo(entry.Target) { UseShellExecute = true });
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(@"shell:AppsFolder\" + entry.Target);
        Process.Start(startInfo);
    }

    private void AddStartApps(List<Entry> shortcuts)
    {
        var combined = new List<Entry>(shortcuts);
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("Get-StartApps | ForEach-Object { \"$($_.Name)`t$($_.AppID)\" }");

            using var process = Process.Start(startInfo);
            if (process == null) return;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000) || process.ExitCode != 0) return;

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf('\t');
                if (separator <= 0 || separator == line.Length - 1) continue;
                string name = line[..separator].Trim();
                string appId = line[(separator + 1)..].Trim();
                if (name.Length > 0 && appId.Length > 0)
                    combined.Add(new Entry(name, appId, true, "Start menu"));
            }
        }
        catch
        {
            // Classic desktop shortcuts remain searchable when Get-StartApps is unavailable.
        }

        Replace(combined);
    }

    private void Replace(IEnumerable<Entry> entries)
    {
        var unique = entries
            .GroupBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderBy(entry => entry.IsAppId).First())
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        lock (_lock) _entries = unique;
        Changed?.Invoke();
    }

    private static List<Entry> ReadStartMenuShortcuts()
    {
        var entries = new List<Entry>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };

        foreach (string root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(path);
                    if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = Path.GetFileNameWithoutExtension(path).Trim();
                    if (name.Length == 0 || LooksLikeMaintenanceShortcut(name)) continue;
                    string location = Path.GetRelativePath(root, Path.GetDirectoryName(path) ?? root);
                    entries.Add(new Entry(name, path, false,
                        location == "." ? "Start menu" : location));
                }
            }
            catch
            {
                // One inaccessible subtree should not disable application search.
            }
        }

        return entries;
    }

    private static bool LooksLikeMaintenanceShortcut(string name) =>
        name.StartsWith("Uninstall ", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Repair ", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(" Help", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Uninstall", StringComparison.OrdinalIgnoreCase);
}
