using System.IO;

namespace AiController.Services;

public record InstalledApp(string Name, string ShortcutPath);

/// <summary>
/// Enumerates installed apps by scanning the Start Menu shortcut folders --
/// the Windows equivalent of rofi's "drun" mode, which reads .desktop files.
/// Deliberately does NOT hook into or read from the Windows Start Menu UI
/// itself: same reasoning as the Linux build's rofi decision (see
/// ai-rofi-launcher.sh) -- the modern Start Menu is a shell-hosted surface,
/// not a plain window, and is likely to have the identical "not a real
/// automatable window" problem that took most of a day to diagnose on
/// Cinnamon. Reading the underlying shortcut files sidesteps that class of
/// bug entirely rather than rediscovering it on a second OS.
/// </summary>
public static class AppEnumerationService
{
    private static readonly string[] ShortcutRoots =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
    };

    public static List<InstalledApp> Enumerate()
    {
        var apps = new List<InstalledApp>();
        foreach (var root in ShortcutRoots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                continue; // some subfolders may be access-restricted; skip rather than fail the whole scan
            }

            foreach (var path in shortcuts)
            {
                apps.Add(new InstalledApp(Path.GetFileNameWithoutExtension(path), path));
            }
        }
        return apps
            .DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Launch a shortcut via the shell (ShellExecute), the same as
    /// double-clicking it -- Windows resolves the .lnk target itself, so
    /// there's no need to parse the shortcut file's binary format directly.
    /// </summary>
    public static void Launch(InstalledApp app)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(app.ShortcutPath) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }
}
