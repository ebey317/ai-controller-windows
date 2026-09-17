using System.Runtime.InteropServices;

namespace AiController.Services;

public enum ProfileContext { Desktop, Browser, Iptv }

/// <summary>
/// Switches the active ControllerProfile based on the foreground window's
/// process -- mirrors the Linux build's per-context profile swap (desktop,
/// browser, and IPTV each get their own AntiMicroX button mapping via
/// controller-profile-switcher.sh). There's no AntiMicroX equivalent on
/// Windows to hook into, so this polls the foreground window's owning process
/// name directly through Win32 and classifies it, the same win32-native
/// approach AppEnumerationService and InputInjector already use elsewhere in
/// this codebase rather than a heavier UI-Automation dependency.
/// </summary>
public sealed class ContextSwitcher : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave" };

    private static readonly HashSet<string> IptvProcesses = new(StringComparer.OrdinalIgnoreCase)
        { "mpv", "vlc", "mpc-hc", "mpc-hc64" };

    private readonly System.Threading.Timer _timer;
    private readonly Dictionary<ProfileContext, Models.ControllerProfile> _profiles;
    private ProfileContext _current = ProfileContext.Desktop;

    /// <summary>Raised whenever the classified context changes, carrying the
    /// profile that should now become active.</summary>
    public event Action<Models.ControllerProfile>? ContextChanged;

    public ContextSwitcher(Dictionary<ProfileContext, Models.ControllerProfile> profiles)
    {
        _profiles = profiles;
        // 500ms is plenty responsive for "I alt-tabbed to a browser" without
        // burning a thread the way a 16ms XInput-style poll would for this.
        _timer = new System.Threading.Timer(Poll, null, 0, 500);
    }

    private void Poll(object? state)
    {
        var context = ClassifyForegroundWindow();
        if (context == _current) return;
        _current = context;
        if (_profiles.TryGetValue(context, out var profile))
            ContextChanged?.Invoke(profile);
    }

    private static ProfileContext ClassifyForegroundWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return ProfileContext.Desktop;

        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            if (BrowserProcesses.Contains(name)) return ProfileContext.Browser;
            if (IptvProcesses.Contains(name)) return ProfileContext.Iptv;
        }
        catch (Exception)
        {
            // Process exited between GetWindowThreadProcessId and GetProcessById,
            // or it's elevated and access is denied -- fall back to desktop rather
            // than let a transient race take down the polling timer.
        }
        return ProfileContext.Desktop;
    }

    public void Dispose() => _timer.Dispose();
}
