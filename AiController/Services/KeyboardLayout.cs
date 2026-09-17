namespace AiController.Services;

/// <summary>
/// Shared QWERTY row/special-key data for both on-screen keyboards (the plain
/// grid one, OnScreenKeyboardWindow, and the mode+pins one, SlideKeyboard) --
/// one layout definition, not two copies that can silently drift apart.
/// </summary>
public static class KeyboardLayout
{
    public const ushort VK_BACK = 0x08;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_SPACE = 0x20;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;

    public static readonly Dictionary<string, ushort> SpecialKeys = new()
    {
        ["Esc"] = VK_ESCAPE, ["Bksp"] = VK_BACK, ["Tab"] = VK_TAB, ["Enter"] = VK_RETURN,
        ["Space"] = VK_SPACE, ["Left"] = VK_LEFT, ["Right"] = VK_RIGHT, ["Up"] = VK_UP, ["Down"] = VK_DOWN,
    };

    public static readonly string[][] RowsLower =
    {
        new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "Bksp" },
        new[] { "Tab", "q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "[", "]", "\\" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", ";", "'", "Enter" },
        new[] { "Shift", "z", "x", "c", "v", "b", "n", "m", ",", ".", "/", "Shift" },
        new[] { "Esc", "Left", "Down", "Up", "Right", "Space" },
    };

    public static readonly string[][] RowsUpper =
    {
        new[] { "~", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "+", "Bksp" },
        new[] { "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "{", "}", "|" },
        new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ":", "\"", "Enter" },
        new[] { "Shift", "Z", "X", "C", "V", "B", "N", "M", "<", ">", "?", "Shift" },
        new[] { "Esc", "Left", "Down", "Up", "Right", "Space" },
    };
}
