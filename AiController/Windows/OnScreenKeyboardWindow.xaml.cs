using System.Windows;
using System.Windows.Controls;
using AiController.Services;

namespace AiController.Windows;

/// <summary>
/// Floating on-screen keyboard -- mirrors slide_keyboard.py's grid-of-buttons
/// design and, more importantly, its two hardest-won lessons from today's
/// Linux debugging: (1) capture the target window once, when the keyboard
/// opens, not per-keystroke (ShowActivated=false means this window itself
/// never becomes foreground, so "whatever's foreground right now" is only
/// meaningful at open time); (2) verify that target before every single
/// keystroke rather than firing blind, and skip-and-log rather than crash
/// if it drifted. Same discipline, entirely new implementation -- nothing
/// about GTK's set_accept_focus or X11 grabs applies here, but the
/// *reasoning* for why this matters is identical.
/// </summary>
public partial class OnScreenKeyboardWindow : Window
{
    private const ushort VK_BACK = 0x08;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;

    private static readonly Dictionary<string, ushort> SpecialKeys = new()
    {
        ["Esc"] = VK_ESCAPE, ["Bksp"] = VK_BACK, ["Tab"] = VK_TAB, ["Enter"] = VK_RETURN,
        ["Space"] = VK_SPACE, ["Left"] = VK_LEFT, ["Right"] = VK_RIGHT, ["Up"] = VK_UP, ["Down"] = VK_DOWN,
    };

    private static readonly string[][] RowsLower =
    {
        new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "Bksp" },
        new[] { "Tab", "q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "[", "]", "\\" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", ";", "'", "Enter" },
        new[] { "Shift", "z", "x", "c", "v", "b", "n", "m", ",", ".", "/", "Shift" },
        new[] { "Esc", "Left", "Down", "Up", "Right", "Space" },
    };

    private static readonly string[][] RowsUpper =
    {
        new[] { "~", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "+", "Bksp" },
        new[] { "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "{", "}", "|" },
        new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ":", "\"", "Enter" },
        new[] { "Shift", "Z", "X", "C", "V", "B", "N", "M", "<", ">", "?", "Shift" },
        new[] { "Esc", "Left", "Down", "Up", "Right", "Space" },
    };

    private IntPtr _focusTargetWindow;
    private bool _shiftOn;

    public OnScreenKeyboardWindow()
    {
        InitializeComponent();
    }

    /// <summary>Call right before Show()/Visibility toggle -- snapshots the
    /// window that should receive every keystroke for this session, the
    /// same moment slide_keyboard.py's _toggle_main_thread does it.</summary>
    public void CaptureFocusTarget()
    {
        _focusTargetWindow = InputInjector.ActiveWindow();
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            CaptureFocusTarget();
            BuildKeys();
            Show();
        }
    }

    private void BuildKeys()
    {
        KeyRows.Children.Clear();
        foreach (var row in (_shiftOn ? RowsUpper : RowsLower))
        {
            var rowPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };
            foreach (var key in row)
            {
                var btn = new System.Windows.Controls.Button { Content = key, Style = (Style)FindResource("KeyButtonStyle") };
                if (key == "Space") btn.MinWidth = 220;
                btn.Click += (_, _) => OnKeyPressed(key);
                rowPanel.Children.Add(btn);
            }
            KeyRows.Children.Add(rowPanel);
        }
    }

    private void OnKeyPressed(string key)
    {
        if (key == "Shift")
        {
            _shiftOn = !_shiftOn;
            BuildKeys();
            return;
        }

        try
        {
            if (SpecialKeys.TryGetValue(key, out var vk))
            {
                InputInjector.GuardedKey(_focusTargetWindow, vk);
            }
            else
            {
                InputInjector.GuardedType(_focusTargetWindow, key);
            }
        }
        catch (InputInjector.FocusLostException)
        {
            // Same as send()'s catch on the Linux build: skip and move on,
            // never fire blind into whatever ended up foreground instead.
        }

        if (_shiftOn)
        {
            _shiftOn = false;
            BuildKeys();
        }
    }
}
