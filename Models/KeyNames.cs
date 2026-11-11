using Avalonia.Input;

namespace DroidPad.Models;

/// <summary>Tuş adlarını saklama (Key.ToString) ve ekranda gösterme biçimleri arasında çevirir.</summary>
public static class KeyNames
{
    public static string Name(Key key) => key.ToString();

    public static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static string Display(string name) => name switch
    {
        "Up" => "↑",
        "Down" => "↓",
        "Left" => "←",
        "Right" => "→",
        "Space" => "Space",
        "Return" or "Enter" => "Enter",
        "LeftShift" or "RightShift" => "Shift",
        "LeftCtrl" or "RightCtrl" => "Ctrl",
        "LeftAlt" or "RightAlt" => "Alt",
        "LWin" or "RWin" => "Win",
        "Tab" => "Tab",
        "Back" => "Backspace",
        "Escape" => "Esc",
        "Delete" => "Del",
        "Insert" => "Ins",
        "Prior" or "PageUp" => "PgUp",
        "Next" or "PageDown" => "PgDn",
        "CapsLock" or "Capital" => "Caps",
        "OemTilde" => "`",
        "OemMinus" => "-",
        "OemPlus" => "=",
        "OemOpenBrackets" => "[",
        "OemCloseBrackets" => "]",
        "OemPipe" or "OemBackslash" => "\\",
        "OemSemicolon" => ";",
        "OemQuotes" => "'",
        "OemComma" => ",",
        "OemPeriod" => ".",
        "OemQuestion" => "/",
        "Add" => "N+",
        "Subtract" => "N-",
        "Multiply" => "N*",
        "Divide" => "N/",
        "Decimal" => "N.",
        _ when name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]) => name[1..],
        _ when name.StartsWith("NumPad", StringComparison.Ordinal) => "N" + name[6..],
        _ when name.StartsWith("Oem", StringComparison.Ordinal) => name[3..],
        _ => name
    };
}
