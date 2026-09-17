using System.Windows.Forms;
using AiController.Models;
using AiController.Windows;

namespace AiController;

/// <summary>
/// System tray presence -- the one way to reach Settings without a
/// controller at all, since there's no Start Menu entry point for a
/// background-only app. Placeholder default icon (System.Drawing.SystemIcons
/// .Application); a real branded .ico is a packaging/asset task, not part
/// of the core-pipeline work this phase is scoped to.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ControllerProfile _profile;

    /// <summary>F13 fix: LegendOverlay had no user-invokable activation path at all --
    /// ToggleVisible() existed but nothing ever called it. The tray menu is the
    /// smallest surface to wire up (no new controller binding, no profile schema
    /// change) without touching existing button-mapping behavior.</summary>
    public TrayIconHost(ControllerProfile profile, Action toggleLegend)
    {
        _profile = profile;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Toggle Legend", null, (_, _) => toggleLegend());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "AI Controller",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenSettings();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_profile);
        window.Show();
        window.Activate();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
