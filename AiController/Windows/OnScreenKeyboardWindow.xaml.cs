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
///
/// W2 fix: ShowActivated="False" (set in the XAML) stops this window's own
/// Show() from focusing it, but not every focus-stealing path -- the true
/// Win32 fix is the WS_EX_NOACTIVATE extended style, applied via
/// NativeWindowHelper once the window's handle exists (SourceInitialized).
/// Deliberately no Owner is set, for the same reason (see NativeWindowHelper's
/// doc comment).
/// </summary>
public partial class OnScreenKeyboardWindow : Window
{
    private IntPtr _focusTargetWindow;
    private bool _shiftOn;

    public OnScreenKeyboardWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeWindowHelper.SetNoActivate(this);
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
        foreach (var row in (_shiftOn ? KeyboardLayout.RowsUpper : KeyboardLayout.RowsLower))
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
            if (KeyboardLayout.SpecialKeys.TryGetValue(key, out var vk))
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
