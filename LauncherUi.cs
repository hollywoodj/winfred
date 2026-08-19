using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Winfred;

/// <summary>Tracks which modifier keys are held while the launcher is open, so
/// the selected row can rewrite its subtitle the way Alfred does.</summary>
public sealed class ModifierState : INotifyPropertyChanged
{
    public static ModifierState Instance { get; } = new();

    private ModifierKeys _keys;

    public ModifierKeys Keys
    {
        get => _keys;
        set
        {
            if (_keys == value) return;
            _keys = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Keys)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tick)));
        }
    }

    /// <summary>Dummy that changes with <see cref="Keys"/> so bindings refresh.</summary>
    public int Tick => (int)_keys;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static void Capture()
    {
        Instance.Keys = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
    }

    public static void Clear() => Instance.Keys = ModifierKeys.None;
}

/// <summary>
/// Selected row + held modifier → alternate action label (Alfred's "hold ⌘ to
/// see what ⌘↩ will do"). Other rows keep their ordinary subtitle.
/// </summary>
public sealed class ActionSubtitleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not ResultItem item) return "";
        bool selected = values[1] is true;
        if (!selected || !Config.Current.Appearance.ShowActionHints)
            return item.Subtitle;

        var keys = ModifierState.Instance.Keys;
        if ((keys & ModifierKeys.Control) != 0 && item.CtrlEnter is { Label.Length: > 0 } ctrl)
            return $"Ctrl+Enter  {ctrl.Label}";
        if ((keys & ModifierKeys.Shift) != 0 && item.ShiftEnter is { Label.Length: > 0 } shift)
            return $"Shift+Enter  {shift.Label}";
        if ((keys & ModifierKeys.Alt) != 0 && item.AltEnter is { Label.Length: > 0 } alt)
            return $"Alt+Enter  {alt.Label}";
        return item.Subtitle;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value is null;
        bool visible = Invert ? isNull : !isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
