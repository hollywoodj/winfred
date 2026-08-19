using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Winfred;

/// <summary>Alfred's "large type": throws the answer on screen big enough to read across a room.</summary>
public static class LargeTypeWindow
{
    private static Window? _window;

    public static void ShowText(string text)
    {
        Close();

        var label = new TextBlock
        {
            Text = text,
            FontSize = 96,
            FontWeight = FontWeights.Light,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF7)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = SystemParameters.WorkArea.Width * 0.8,
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x20, 0x22, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(48, 32, 48, 32),
            Child = label,
        };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new Grid { Margin = new Thickness(24), Children = { border } },
        };

        _window.MouseLeftButtonDown += (_, _) => Close();
        _window.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space) Close();
        };
        _window.Deactivated += (_, _) => Close();
        _window.Show();
        _window.Activate();
    }

    public static void Close()
    {
        var window = _window;
        _window = null;
        try { window?.Close(); } catch { /* already closing */ }
    }
}
