namespace QuickShot;

/// <summary>
/// Parse chuỗi hotkey dạng "Ctrl+Alt+R" (từ settings.json) thành (Mod, Keys)
/// dùng được với HotkeyWindow.Register.
/// </summary>
public static class HotkeyParser
{
    public static bool TryParse(string spec, out HotkeyWindow.Mod modifiers, out Keys key)
    {
        modifiers = default;
        key = default;

        if (string.IsNullOrWhiteSpace(spec))
            return false;

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            HotkeyWindow.Mod? m = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyWindow.Mod.Control,
                "alt" => HotkeyWindow.Mod.Alt,
                "shift" => HotkeyWindow.Mod.Shift,
                "win" or "windows" => HotkeyWindow.Mod.Win,
                _ => null,
            };

            if (m is null)
                return false;

            modifiers |= m.Value;
        }

        return Enum.TryParse(parts[^1], ignoreCase: true, out key);
    }
}
