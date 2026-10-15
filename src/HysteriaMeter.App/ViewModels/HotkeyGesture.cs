using System.Windows.Input;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Un atajo global como "Ctrl+Shift+L". Pide al menos un modificador (Ctrl, Alt o Shift) salvo las teclas F,
/// para no robarle al juego una letra suelta.
/// </summary>
public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    private static readonly ModifierKeys Allowed = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift;

    /// <summary>El atajo para esta combinación, o <c>null</c> si no sirve como atajo global.</summary>
    public static HotkeyGesture? From(ModifierKeys modifiers, Key key)
    {
        modifiers &= Allowed;
        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        var isModifierOnly = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None;
        if (isModifierOnly || (modifiers == ModifierKeys.None && !isFunctionKey) || (modifiers == ModifierKeys.Shift && !isFunctionKey))
        {
            return null;
        }

        return new HotkeyGesture(modifiers, key);
    }

    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modifiers = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CTRL": modifiers |= ModifierKeys.Control; break;
                case "ALT": modifiers |= ModifierKeys.Alt; break;
                case "SHIFT": modifiers |= ModifierKeys.Shift; break;
                default: return null;
            }
        }

        return parts.Length > 0 && KeyFromText(parts[^1]) is { } key ? From(modifiers, key) : null;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyText(Key));
        return string.Join('+', parts);
    }

    private static string KeyText(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };

    private static Key? KeyFromText(string text) =>
        text.Length == 1 && char.IsAsciiDigit(text[0]) ? Key.D0 + (text[0] - '0')
        : Enum.TryParse<Key>(text, ignoreCase: true, out var key) && Enum.IsDefined(key) && !int.TryParse(text, out _) ? key
        : null;
}
