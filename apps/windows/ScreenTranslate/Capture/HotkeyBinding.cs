using System.Windows.Input;

namespace ScreenTranslate.Capture;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    NoRepeat = 0x4000
}

public sealed record HotkeyBinding(HotkeyModifiers Modifiers, Key Key)
{
    public static bool TryParse(string? value, out HotkeyBinding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        Key key = Key.None;
        foreach (var token in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
            }

            if (key != Key.None || !TryParseKey(token, out key))
            {
                return false;
            }
        }

        if (modifiers == HotkeyModifiers.None || key == Key.None)
        {
            return false;
        }

        binding = new HotkeyBinding(modifiers | HotkeyModifiers.NoRepeat, key);
        return true;
    }

    internal static bool TryParseKey(string? token, out Key key)
    {
        key = Key.None;
        // Enum.TryParse also accepts numbers and comma-separated enum values.
        // Only an actual named WPF key is a valid configuration token.
        if (string.IsNullOrWhiteSpace(token)
            || !Enum.GetNames<Key>().Contains(token, StringComparer.OrdinalIgnoreCase)
            || !Enum.TryParse(token, ignoreCase: true, out key)
            || !Enum.IsDefined(key)
            || key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
                or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.F12)
        {
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return virtualKey is > 0 and < 0xFF;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}
