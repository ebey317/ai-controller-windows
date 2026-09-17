using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AiController.Models;
using AiController.Services;

namespace AiController.Windows;

/// <summary>
/// Read-only HUD strip showing the active profile's button legend near the
/// cursor -- mirrors controller-legend.py's floating overlay. Unlike the
/// Linux HUD, this never parses a separate on-disk AntiMicroX profile file to
/// stay in sync: ControllerProfile in this app already IS the single source
/// of truth for bindings, so there's no second copy of the mapping that can
/// drift the way controller-legend.py's hand-typed fallback list once did.
///
/// Never-activate + no-Owner, same as OnScreenKeyboardWindow (W2) -- a legend
/// HUD stealing focus while the operator is mid-action in another window would
/// be exactly the bug this pattern exists to prevent.
/// </summary>
public partial class LegendOverlay : Window
{
    private readonly DispatcherTimer _followTimer;
    private ControllerProfile _profile;

    public LegendOverlay(ControllerProfile profile)
    {
        InitializeComponent();
        _profile = profile;
        SourceInitialized += (_, _) => NativeWindowHelper.SetNoActivate(this);
        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _followTimer.Tick += (_, _) => FollowCursor();
    }

    /// <summary>ContextSwitcher swaps profiles at runtime -- keep the legend in
    /// sync with whichever one is currently active.</summary>
    public void SetProfile(ControllerProfile profile)
    {
        _profile = profile;
        if (IsVisible) BuildLegend();
    }

    private void FollowCursor()
    {
        var pos = System.Windows.Forms.Cursor.Position;
        Left = pos.X + 16;
        Top = pos.Y + 16;
    }

    private void BuildLegend()
    {
        LegendItems.Children.Clear();
        foreach (var input in Enum.GetValues<ControllerInput>())
        {
            var action = _profile.Resolve(input);
            if (action is NullAction) continue;

            LegendItems.Children.Add(new TextBlock
            {
                Text = $"{input}: {Describe(action)}",
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                Margin = new Thickness(8, 2, 8, 2),
            });
        }
    }

    private static string Describe(ButtonAction action) => action switch
    {
        KeyAction k => $"Key {k.KeyText}",
        MouseAction m => $"{m.Kind} ({m.Button})",
        VoiceAction => "Voice",
        KeyboardAction kb => kb.Mode == KeyboardMode.Slide ? "Slide Kbd" : "Keyboard",
        LaunchAction l => string.IsNullOrEmpty(l.AppTarget) ? "Launcher" : l.AppTarget,
        _ => "",
    };

    public void ToggleVisible()
    {
        if (IsVisible)
        {
            Hide();
            _followTimer.Stop();
        }
        else
        {
            BuildLegend();
            FollowCursor();
            Show();
            _followTimer.Start();
        }
    }
}
