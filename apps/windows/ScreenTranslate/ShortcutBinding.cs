using System.Windows.Input;
using ScreenTranslate.Capture;

namespace ScreenTranslate;

internal static class ShortcutBinding
{
    internal static bool TryParse(string? text, out HotkeyBinding? binding)
    {
        if (HotkeyBinding.TryParse(text, out binding)) return true;
        if (!HotkeyBinding.TryParseKey(text?.Trim(), out var key)) return false;
        // Bare letters would steal ordinary typing. Allow only deliberately chosen
        // navigation/function keys; F12 is reserved by Windows for debuggers.
        if (key is not (Key.Home or Key.End or Key.Insert or Key.Pause or Key.Scroll)
            && key is not (>= Key.F1 and <= Key.F11)) return false;
        binding = new HotkeyBinding(HotkeyModifiers.NoRepeat, key);
        return true;
    }
}
