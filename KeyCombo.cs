using System.Runtime.InteropServices;
using System.Text;

namespace Winfred;

/// <summary>A modifier + key combination, e.g. "Ctrl+Space" or "Ctrl+\", in a form both
/// the global hook and the synthetic-keystroke sender understand.</summary>
public sealed class KeyCombo
{
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public bool Win { get; init; }
    /// <summary>Virtual-key code of the non-modifier key.</summary>
    public int Vk { get; init; }

    public bool HasModifier => Ctrl || Alt || Shift || Win;

    public static bool TryParse(string text, out KeyCombo combo)
    {
        combo = new KeyCombo();
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        int vk = 0;

        // Split on '+' but keep a trailing literal '+' as the key itself.
        var parts = new List<string>();
        var buffer = new StringBuilder();
        foreach (char c in text.Trim())
        {
            if (c == '+' && buffer.Length > 0) { parts.Add(buffer.ToString()); buffer.Clear(); }
            else if (c == '+') parts.Add("+");
            else buffer.Append(c);
        }
        if (buffer.Length > 0) parts.Add(buffer.ToString());

        foreach (var raw in parts)
        {
            string part = raw.Trim();
            if (part.Length == 0) continue;
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control" or "ctl": ctrl = true; continue;
                case "alt" or "option": alt = true; continue;
                case "shift": shift = true; continue;
                case "win" or "cmd" or "command" or "super" or "meta": win = true; continue;
            }
            int parsed = KeyToVk(part);
            if (parsed == 0) return false;
            vk = parsed;
        }

        if (vk == 0) return false;
        combo = new KeyCombo { Ctrl = ctrl, Alt = alt, Shift = shift, Win = win, Vk = vk };
        return true;
    }

    private static int KeyToVk(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "space": return 0x20;
            case "enter" or "return": return 0x0D;
            case "tab": return 0x09;
            case "esc" or "escape": return 0x1B;
            case "backspace" or "back": return 0x08;
            case "delete" or "del": return 0x2E;
            case "insert" or "ins": return 0x2D;
            case "home": return 0x24;
            case "end": return 0x23;
            case "pageup" or "pgup": return 0x21;
            case "pagedown" or "pgdn": return 0x22;
            case "left": return 0x25;
            case "up": return 0x26;
            case "right": return 0x27;
            case "down": return 0x28;
            case "capslock": return 0x14;
        }

        if (name.Length > 1 && char.ToLowerInvariant(name[0]) == 'f' && int.TryParse(name[1..], out int fn)
            && fn is >= 1 and <= 24)
            return 0x6F + fn; // VK_F1 == 0x70

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
            short scan = VkKeyScanW(name[0]);
            if (scan != -1) return scan & 0xFF; // punctuation like '\' or ';'
        }
        return 0;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(VkName(Vk));
        return string.Join("+", parts);
    }

    public static string VkName(int vk)
    {
        switch (vk)
        {
            case 0x20: return "Space";
            case 0x0D: return "Enter";
            case 0x09: return "Tab";
            case 0x1B: return "Esc";
            case 0x08: return "Backspace";
            case 0x2E: return "Delete";
            case 0x2D: return "Insert";
            case 0x24: return "Home";
            case 0x23: return "End";
            case 0x21: return "PageUp";
            case 0x22: return "PageDown";
            case 0x25: return "Left";
            case 0x26: return "Up";
            case 0x27: return "Right";
            case 0x28: return "Down";
        }
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();

        uint character = MapVirtualKeyW((uint)vk, 2 /* MAPVK_VK_TO_CHAR */) & 0x7FFF;
        return character != 0 ? ((char)character).ToString() : $"VK{vk:X2}";
    }

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);
}
