using System.Diagnostics;

namespace DroidPad.Services;

/// <summary>
/// Shift'li karakterin sistemden (KeySymbol/TextInput) gelmediği durumlar için yedek:
/// fiziksel tuş + etkin klavye düzeninden karakteri çıkarır. Yalnızca ASCII, Türkçe Q ve ABD düzenleri.
/// </summary>
public static class KeyboardLayout
{
    private static readonly Lazy<bool> IsTurkish = new(Detect);

    private static bool Detect()
    {
        // En son kullanılan kaynak (mru-sources) boşsa tanımlı kaynaklardan ilkine bakılır.
        foreach (var key in new[] { "mru-sources", "sources" })
        {
            var o = Gsettings(key);
            var i = o?.IndexOf("'xkb'", StringComparison.Ordinal) ?? -1;
            if (i >= 0) return o!.AsSpan(i).StartsWith("'xkb', 'tr");
        }
        return false;
    }

    private static string? Gsettings(string key)
    {
        try
        {
            var psi = new ProcessStartInfo("gsettings", $"get org.gnome.desktop.input-sources {key}")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            return p.WaitForExit(1500) ? o : null;
        }
        catch { return null; }
    }

    // Avalonia PhysicalKey adı → Shift ile üretilen karakter
    private static readonly Dictionary<string, char> Tr = new()
    {
        ["Backquote"] = '"', ["Digit1"] = '!', ["Digit2"] = '\'', ["Digit3"] = '^', ["Digit4"] = '+',
        ["Digit5"] = '%', ["Digit6"] = '&', ["Digit7"] = '/', ["Digit8"] = '(', ["Digit9"] = ')',
        ["Digit0"] = '=', ["Minus"] = '?', ["Equal"] = '_', ["Backslash"] = ';', ["Slash"] = ':',
        ["Comma"] = '\0', ["IntlBackslash"] = '>',
    };

    private static readonly Dictionary<string, char> Us = new()
    {
        ["Backquote"] = '~', ["Digit1"] = '!', ["Digit2"] = '@', ["Digit3"] = '#', ["Digit4"] = '$',
        ["Digit5"] = '%', ["Digit6"] = '^', ["Digit7"] = '&', ["Digit8"] = '*', ["Digit9"] = '(',
        ["Digit0"] = ')', ["Minus"] = '_', ["Equal"] = '+', ["BracketLeft"] = '{', ["BracketRight"] = '}',
        ["Backslash"] = '|', ["Semicolon"] = ':', ["Quote"] = '"', ["Comma"] = '<', ["Period"] = '>',
        ["Slash"] = '?',
    };

    public static string? Shifted(string physicalKey)
    {
        var map = IsTurkish.Value ? Tr : Us;
        return map.TryGetValue(physicalKey, out var c) && c != '\0' ? c.ToString() : null;
    }
}
