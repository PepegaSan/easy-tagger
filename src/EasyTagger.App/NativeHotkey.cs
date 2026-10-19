using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace EasyTagger.App;

static class NativeHotkey
{
    public const int Id = 1;
    public const int WmHotkey = 0x0312;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = ModNoRepeat;
        virtualKey = 0;
        foreach (var part in (text ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "Strg" and "Umschalt" are the German names the keyboard hint uses.
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                case "strg":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                case "umschalt":
                    modifiers |= ModShift;
                    break;
                case "win":
                    modifiers |= ModWin;
                    break;
                default:
                    if (part.Length == 1)
                        virtualKey = char.ToUpperInvariant(part[0]);
                    else if (Enum.TryParse<Key>(part, true, out var key))
                        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    else
                        // An unknown word must not be dropped silently, or the hotkey ends up as something else.
                        return false;
                    break;
            }
        }

        return virtualKey != 0;
    }

    // Writes a pressed combination the way TryParse reads it. A lone modifier gives "", and so does
    // a key that typing needs: Backspace or Shift+E as a global hotkey would swallow that key everywhere.
    public static string Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return "";
        var chord = modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows);
        if (chord == ModifierKeys.None && key is not (>= Key.F1 and <= Key.F24))
            return "";
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
        return string.Join("+", parts);
    }
}
