using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiController.Services;

/// <summary>
/// Shared Win32 interop for windows that must never steal foreground focus --
/// the on-screen keyboards and the legend HUD. W2 fix: ShowActivated="False"
/// alone stops WPF's own Show()/Activate() calls from focusing the window, but
/// it does not stop the window MANAGER from occasionally giving it focus (e.g.
/// certain alt-tab or click-through edge cases) the way the true Win32
/// WS_EX_NOACTIVATE extended style does. Applying that style directly via
/// SetWindowLong is the same fix real click-through/overlay Win32 apps use.
///
/// Deliberately does not set Owner on these windows: an owned, topmost,
/// never-activated utility window can still hand focus back to its owner on
/// Show()/Close() in some WPF window-management paths, which is exactly the
/// focus-stealing W2 exists to prevent. Leave Owner unset.
/// </summary>
public static class NativeWindowHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Call from the window's SourceInitialized handler (the handle
    /// doesn't exist yet in the constructor) to mark it WS_EX_NOACTIVATE.</summary>
    public static void SetNoActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
    }
}
