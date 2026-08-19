using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace Winfred;

public partial class MainWindow : Window
{
    public SearchEngine Engine { get; } = new();

    private string _lastQuery = "";
    private bool _openingSettings;

    public MainWindow()
    {
        InitializeComponent();
        ApplyAppearance();
        Engine.ResultsChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) RunQuery();
        });
    }

    /// <summary>Pushes Appearance settings into the window; safe to call whenever config changes.</summary>
    public void ApplyAppearance()
    {
        var appearance = Config.Current.Appearance;
        Theme.Apply(appearance.Theme);

        Width = appearance.WindowWidth;
        Placeholder.Text = appearance.Placeholder;
        Results.MaxHeight = Math.Max(120, appearance.MaxResults * 50 * appearance.FontScale);
        // The hat mark rides alongside the input, so it scales with it.
        HatMark.Width = HatMark.Height = 40.0 * appearance.FontScale;

        var resources = Application.Current.Resources;
        resources["WinInputSize"] = 24.0 * appearance.FontScale;
        resources["WinIconSize"] = 20.0 * appearance.FontScale;
        resources["WinResultIconSize"] = 36.0 * appearance.FontScale;
        resources["WinTitleSize"] = 18.0 * appearance.FontScale;
        resources["WinSubSize"] = 12.0 * appearance.FontScale;
        resources["WinHintSize"] = 11.0 * appearance.FontScale;
        resources["WinShortcutSize"] = 16.0 * appearance.FontScale;

        if (IsVisible) RunQuery();
    }

    public void Toggle()
    {
        if (IsVisible) HideLauncher();
        else ShowLauncher();
    }

    public void ShowLauncher()
    {
        Input.Text = "";
        RunQuery();
        PositionOnActiveScreen();
        Show();
        ForceForeground();
        Input.Focus();
        Keyboard.Focus(Input);
    }

    public void HideLauncher()
    {
        ModifierState.Clear();
        Hide();
    }

    private void PositionOnActiveScreen()
    {
        var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
        var dpi = VisualTreeHelper.GetDpi(this);
        double x = screen.WorkingArea.X / dpi.DpiScaleX;
        double y = screen.WorkingArea.Y / dpi.DpiScaleY;
        double w = screen.WorkingArea.Width / dpi.DpiScaleX;
        double h = screen.WorkingArea.Height / dpi.DpiScaleY;
        Left = x + (w - Width) / 2;
        Top = y + h * (Config.Current.Appearance.TopOffsetPercent / 100.0);
    }

    /// <summary>A window shown from a background app doesn't reliably get focus;
    /// briefly attach to the foreground thread's input queue so it does.</summary>
    private void ForceForeground()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        nint foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint ourThread = GetCurrentThreadId();
        if (foreground != 0 && foregroundThread != ourThread)
        {
            AttachThreadInput(foregroundThread, ourThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(foregroundThread, ourThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }
        Activate();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_openingSettings) return;
        HideLauncher();
    }

    private void Cog_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenSettings();
    }

    private void OpenSettings()
    {
        _openingSettings = true;
        try
        {
            SearchEngine.OpenSettings?.Invoke();
        }
        finally
        {
            _openingSettings = false;
        }
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RunQuery();
    }

    private void RunQuery()
    {
        _lastQuery = Input.Text;
        var items = Engine.Query(Input.Text, () => Dispatcher.BeginInvoke(RunQuery));
        AssignShortcuts(items);
        Results.ItemsSource = items;
        bool any = items.Count > 0;
        Results.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (any)
            Results.SelectedIndex = 0;
        UpdateTypeahead();
    }

    private static void AssignShortcuts(List<ResultItem> items)
    {
        foreach (var item in items) item.Shortcut = "";
        if (!Config.Current.Appearance.ShowResultShortcuts) return;
        for (int i = 0; i < items.Count && i < 9; i++)
            items[i].Shortcut = (i + 1).ToString();
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTypeahead();

    /// <summary>Ghost-completes the selected result's Tab expansion in the search field, like Alfred.</summary>
    private void UpdateTypeahead()
    {
        Typeahead.Inlines.Clear();
        string typed = Input.Text;
        if (typed.Length == 0 ||
            Results.SelectedItem is not ResultItem { AutoComplete: { Length: > 0 } completion } ||
            !completion.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ||
            completion.Length <= typed.Length)
            return;

        var placeholder = TryFindResource("WinPlaceholder") as Brush ?? Brushes.Gray;
        Typeahead.Inlines.Add(new Run(typed) { Foreground = Brushes.Transparent });
        Typeahead.Inlines.Add(new Run(completion[typed.Length..]) { Foreground = placeholder });
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        // With Alt held WPF reports Key.System and puts the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierState.Capture();

        // Ctrl+, opens settings, like Alfred's Cmd+,.
        if ((modifiers & ModifierKeys.Control) != 0 && key is Key.OemComma or Key.Comma)
        {
            e.Handled = true;
            OpenSettings();
            return;
        }

        // Ctrl+1…9 runs that result directly, like Alfred's Cmd+number.
        if ((modifiers & ModifierKeys.Control) != 0 && key is >= Key.D1 and <= Key.D9)
        {
            int index = key - Key.D1;
            if (index < Results.Items.Count)
            {
                Results.SelectedIndex = index;
                ExecuteSelected(false, false, false);
            }
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Escape:
                HideLauncher();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                MoveSelection(5);
                e.Handled = true;
                break;
            case Key.PageUp:
                MoveSelection(-5);
                e.Handled = true;
                break;
            case Key.Tab:
                AutoComplete();
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelected(
                    (modifiers & ModifierKeys.Control) != 0,
                    (modifiers & ModifierKeys.Shift) != 0,
                    (modifiers & ModifierKeys.Alt) != 0);
                e.Handled = true;
                break;
        }
    }

    private void Window_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e) =>
        ModifierState.Capture();

    private void AutoComplete()
    {
        if (Results.SelectedItem is not ResultItem { AutoComplete: { Length: > 0 } completion }) return;
        Input.Text = completion;
        Input.CaretIndex = Input.Text.Length;
    }

    private void MoveSelection(int delta)
    {
        if (Results.Items.Count == 0) return;
        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, Results.Items.Count - 1);
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void Results_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) return;
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject)
            is ListBoxItem row)
            Results.SelectedItem = row.DataContext;
    }

    private void Results_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject)
            is not ListBoxItem row)
            return;

        Results.SelectedItem = row.DataContext;
        e.Handled = true;
        ExecuteSelected(false, false, false);
    }

    private void ExecuteSelected(bool ctrl, bool shift, bool alt)
    {
        if (Results.SelectedItem is not ResultItem item) return;
        var action = item.ActionFor(ctrl, shift, alt);
        if (action == null) return;

        if (item.Uid != null) Usage.Record(_lastQuery, item.Uid);
        if (action.HidesWindow) HideLauncher();
        try
        {
            action.Run();
        }
        catch (Exception ex)
        {
            Notifier.Notify(ex.Message);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
