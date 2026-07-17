using Microsoft.Win32;

namespace AiController.Services;

/// <summary>
/// Toggle "start on login" via the per-user Registry Run key -- the simplest
/// supported autostart mechanism for a plain WinExe (no Windows Service, no
/// scheduled task). HKEY_CURRENT_USER, not HKEY_LOCAL_MACHINE: no admin
/// rights needed, and it only affects the user who enabled it.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AiController";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
