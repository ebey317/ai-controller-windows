using System.Runtime.InteropServices;

namespace AiController.Services;

/// <summary>
/// Win32 SendInput wrapper -- the Windows equivalent of xdotool on the Linux
/// build. Also ports the *concept* (not the code -- there's no shared runtime
/// between the two) of focus_guard.py from ~/ai-controller/scripts/focus_guard.py:
/// verify the intended target is actually foreground before injecting, and
/// again after, so a focus change mid-injection is caught instead of silently
/// typing into the wrong window. That was a real, live-reproduced bug on
/// Linux (a typed sequence once landed in an unrelated terminal instead of
/// the intended dialog) -- there's no reason to assume Windows is immune to
/// the same class of race, so the same discipline is worth carrying over even
/// though the API underneath it is completely different.
/// </summary>
public static class InputInjector
{
    private const int INPUT_KEYBOARD = 1;
    private const int INPUT_MOUSE = 0;

    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// True if <paramref name="current"/> is not a *different, identifiable*
    /// window than <paramref name="target"/>. Mirrors focus_guard.py's
    /// _focus_acceptable: an exact match is the normal case; IntPtr.Zero
    /// (no foreground window resolvable) is also treated as acceptable,
    /// the same reasoning as the rofi case on Linux -- some windows
    /// (modern shell-hosted surfaces, UWP popups) may not report cleanly via
    /// GetForegroundWindow, and there's no *wrong* identifiable window to
    /// mistype into in that state. A different, concrete HWND is the one
    /// case this actually guards against.
    /// </summary>
    private static bool FocusAcceptable(IntPtr current, IntPtr target) =>
        current == target || current == IntPtr.Zero;

    /// <summary>
    /// Re-activate <paramref name="target"/> until confirmed foreground (or
    /// unreadable -- see FocusAcceptable), or give up. Mirrors
    /// focus_guard.ensure_focus's retry loop.
    /// </summary>
    public static bool EnsureForeground(IntPtr target, int retries = 6, int delayMs = 40)
    {
        if (target == IntPtr.Zero) return false;
        for (int i = 0; i < retries; i++)
        {
            if (FocusAcceptable(GetForegroundWindow(), target)) return true;
            SetForegroundWindow(target);
            Thread.Sleep(delayMs);
        }
        return FocusAcceptable(GetForegroundWindow(), target);
    }

    /// <summary>
    /// Exception thrown instead of firing blind when focus can't be
    /// confirmed before injection, or drifted away during it. Mirrors
    /// focus_guard.FocusLostError -- callers decide the fallback (skip,
    /// clipboard, retry); this class only ever refuses to guess.
    /// </summary>
    public class FocusLostException : Exception
    {
        public FocusLostException(string message) : base(message) { }
    }

    /// <summary>
    /// Type literal text into <paramref name="target"/>, verified focused
    /// before and after. Uses KEYEVENTF_UNICODE so it sends any Unicode
    /// character directly (surrogate pairs included) rather than mapping
    /// through virtual-key codes and the current keyboard layout -- the
    /// direct equivalent of xdotool type's --clearmodifiers behavior.
    /// </summary>
    public static void GuardedType(IntPtr target, string text)
    {
        if (!EnsureForeground(target))
            throw new FocusLostException($"could not focus {target} before typing");

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(UnicodeKeyInput(c, keyUp: false));
            inputs.Add(UnicodeKeyInput(c, keyUp: true));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());

        if (!FocusAcceptable(GetForegroundWindow(), target))
            throw new FocusLostException($"focus left {target} during typing");
    }

    private static INPUT UnicodeKeyInput(char c, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
    }

    private static INPUT VirtualKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
    }

    /// <summary>
    /// Send a named key by virtual-key code (Backspace, Enter, arrows, etc.)
    /// -- the equivalent of xdotool key, as opposed to GuardedType's
    /// equivalent of xdotool type for literal Unicode text. Optionally holds
    /// modifier virtual-key codes down for the duration (e.g. VK_SHIFT for a
    /// shifted arrow-key selection), mirroring send()'s ctrl/alt handling on
    /// the Linux/slide_keyboard.py build.
    /// </summary>
    public static void GuardedKey(IntPtr target, ushort vk, IEnumerable<ushort>? modifiers = null)
    {
        if (!EnsureForeground(target))
            throw new FocusLostException($"could not focus {target} before key {vk}");

        var mods = modifiers?.ToList() ?? new List<ushort>();
        var inputs = new List<INPUT>();
        foreach (var m in mods) inputs.Add(VirtualKeyInput(m, keyUp: false));
        inputs.Add(VirtualKeyInput(vk, keyUp: false));
        inputs.Add(VirtualKeyInput(vk, keyUp: true));
        for (int i = mods.Count - 1; i >= 0; i--) inputs.Add(VirtualKeyInput(mods[i], keyUp: true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());

        if (!FocusAcceptable(GetForegroundWindow(), target))
            throw new FocusLostException($"focus left {target} during key {vk}");
    }

    /// <summary>Left mouse click at the current cursor position, verified the same way as GuardedType.</summary>
    public static void GuardedClick(IntPtr target)
    {
        if (!EnsureForeground(target))
            throw new FocusLostException($"could not focus {target} before click");

        var down = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } };
        var up = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());

        if (!FocusAcceptable(GetForegroundWindow(), target))
            throw new FocusLostException($"focus left {target} during click");
    }

    public static IntPtr ActiveWindow() => GetForegroundWindow();
}
