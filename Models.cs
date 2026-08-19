namespace Winfred;

public enum ResultKind
{
    Hint,
    Web,
    Calculator,
    Application,
    File,
    Bookmark,
    Note,
    Setting,
    Credential,
    Action,
    Error,
}

/// <summary>One thing a result can do, bound to Enter or a modifier + Enter.</summary>
public sealed class ResultAction
{
    public string Label { get; init; } = "";
    public Action Run { get; init; } = () => { };
    /// <summary>False keeps the launcher open after running (used by hints and retries).</summary>
    public bool HidesWindow { get; init; } = true;

    public static ResultAction Of(string label, Action run, bool hidesWindow = true) =>
        new() { Label = label, Run = run, HidesWindow = hidesWindow };
}

public class ResultItem
{
    public string Icon { get; init; } = "🔍";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public ResultKind Kind { get; init; } = ResultKind.Action;

    /// <summary>Stable identity used to learn which results this user picks. Null = never learned.</summary>
    public string? Uid { get; init; }

    /// <summary>Relevance, higher first. Providers set a raw score; the engine adds a usage boost.</summary>
    public double Score { get; set; }

    /// <summary>Providers that must stay at the top regardless of score (calculator, keyword hits).</summary>
    public int Priority { get; init; }

    /// <summary>Text Tab expands the query to, Alfred-style. Null disables Tab for this row.</summary>
    public string? AutoComplete { get; init; }

    public ResultAction? Enter { get; init; }
    public ResultAction? CtrlEnter { get; init; }
    public ResultAction? ShiftEnter { get; init; }
    public ResultAction? AltEnter { get; init; }

    /// <summary>Right-hand hint text, e.g. "⏎ open · ⌃⏎ reveal". Computed when not set.</summary>
    public string Hint => _hint ??= BuildHint();
    private string? _hint;

    private string BuildHint()
    {
        if (!Config.Current.Appearance.ShowActionHints) return "";
        var parts = new List<string>();
        if (Enter is { Label.Length: > 0 }) parts.Add($"⏎ {Enter.Label}");
        if (CtrlEnter is { Label.Length: > 0 }) parts.Add($"^⏎ {CtrlEnter.Label}");
        if (ShiftEnter is { Label.Length: > 0 }) parts.Add($"⇧⏎ {ShiftEnter.Label}");
        if (AltEnter is { Label.Length: > 0 }) parts.Add($"⌥⏎ {AltEnter.Label}");
        return string.Join("   ", parts);
    }

    public ResultAction? ActionFor(bool ctrl, bool shift, bool alt)
    {
        if (ctrl && CtrlEnter != null) return CtrlEnter;
        if (shift && ShiftEnter != null) return ShiftEnter;
        if (alt && AltEnter != null) return AltEnter;
        return Enter;
    }

    /// <summary>A non-actionable row that leaves the launcher open.</summary>
    public static ResultItem Info(string icon, string title, string subtitle, ResultKind kind = ResultKind.Hint) =>
        new() { Icon = icon, Title = title, Subtitle = subtitle, Kind = kind };
}

public static class Notifier
{
    public static Action<string>? Handler;
    public static void Notify(string message) => Handler?.Invoke(message);
}

/// <summary>Copies text to the clipboard and clears it again after a delay (unless the
/// user has since copied something else) so passwords don't linger.</summary>
public static class ClipboardGuard
{
    private static System.Threading.Timer? _timer;
    private static string? _lastCopied;

    /// <summary>Copies without a timed wipe — for non-secret text like paths and URLs.</summary>
    public static void CopyPlain(string text) => SetClipboard(text);

    public static void Copy(string text, int clearAfterSeconds = 45)
    {
        SetClipboard(text);
        _lastCopied = text;
        _timer?.Dispose();
        if (clearAfterSeconds <= 0) return;
        _timer = new System.Threading.Timer(_ =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (System.Windows.Clipboard.GetText() == _lastCopied)
                        System.Windows.Clipboard.Clear();
                }
                catch
                {
                    // clipboard is owned by another app right now — nothing to clear
                }
            });
        }, null, clearAfterSeconds * 1000, Timeout.Infinite);
    }

    private static void SetClipboard(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text);
                return;
            }
            catch
            {
                Thread.Sleep(50); // clipboard briefly locked by another process
            }
        }
    }
}
