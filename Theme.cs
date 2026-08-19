using System.Windows;
using System.Windows.Media;

namespace Winfred;

/// <summary>Swaps the brushes the launcher and settings window bind to as DynamicResource.</summary>
public static class Theme
{
    private sealed record Palette(
        string Bg, string Panel, string PanelAlt, string Border, string Text,
        string Subtext, string Placeholder, string Hover, string Selected, string Accent, string Danger);

    private static readonly Palette Dark = new(
        "#F5202230", "#FF171A26", "#FF1E2233", "#33FFFFFF", "#FFF2F2F7",
        "#FF99A0B8", "#55FFFFFF", "#FF2A3048", "#FF3D4666", "#FF5B8CFF", "#FFE06C75");

    private static readonly Palette Midnight = new(
        "#F5000000", "#FF0A0A0C", "#FF141418", "#28FFFFFF", "#FFEDEDED",
        "#FF8A8A96", "#4DFFFFFF", "#FF1C1C22", "#FF2C2C36", "#FF7AA2F7", "#FFE06C75");

    private static readonly Palette Light = new(
        "#F7FFFFFF", "#FFF6F6F8", "#FFFFFFFF", "#33000000", "#FF16171A",
        "#FF5C6070", "#66000000", "#FFE9ECF5", "#FFD6DDF0", "#FF2563EB", "#FFC0392B");

    public static void Apply(string name)
    {
        var palette = name.ToLowerInvariant() switch
        {
            "light" => Light,
            "midnight" => Midnight,
            _ => Dark,
        };

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        void Set(string key, string color) =>
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

        Set("WinBg", palette.Bg);
        Set("WinPanel", palette.Panel);
        Set("WinPanelAlt", palette.PanelAlt);
        Set("WinBorder", palette.Border);
        Set("WinText", palette.Text);
        Set("WinSubtext", palette.Subtext);
        Set("WinPlaceholder", palette.Placeholder);
        Set("WinHover", palette.Hover);
        Set("WinSelected", palette.Selected);
        Set("WinAccent", palette.Accent);
        Set("WinDanger", palette.Danger);
    }

    public static string[] Names => new[] { "dark", "midnight", "light" };
}
