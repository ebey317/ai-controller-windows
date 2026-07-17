using System.Windows;
using System.Windows.Input;
using AiController.Services;

namespace AiController.Windows;

/// <summary>
/// App launcher -- the Windows equivalent of rofi on the Linux build.
/// Unlike rofi (which needs focus_guard's None-tolerant grab handling
/// because it never becomes the EWMH-visible active window), this is a
/// plain WPF Window: it becomes the real Win32 foreground window through
/// normal Show()/Activate(), so there's no equivalent focus-tracking
/// problem to work around here. That asymmetry between the two platforms
/// is real, not an oversight -- rofi's behavior was a property of X11
/// grabs and shell-hosted popups, which Win32 doesn't have.
/// </summary>
public partial class LauncherWindow : Window
{
    private List<InstalledApp> _allApps = new();

    public LauncherWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _allApps = AppEnumerationService.Enumerate();
            ResultsList.ItemsSource = _allApps.Select(a => a.Name).Take(50);
            SearchBox.Focus();
        };
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allApps
            : _allApps.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        ResultsList.ItemsSource = filtered.Select(a => a.Name).Take(50).ToList();
        if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (ResultsList.SelectedIndex < ResultsList.Items.Count - 1) ResultsList.SelectedIndex++;
                e.Handled = true;
                break;
            case Key.Up:
                if (ResultsList.SelectedIndex > 0) ResultsList.SelectedIndex--;
                e.Handled = true;
                break;
            case Key.Enter:
                LaunchSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();

    private void LaunchSelected()
    {
        if (ResultsList.SelectedItem is not string name) return;
        var app = _allApps.FirstOrDefault(a => a.Name == name);
        if (app == null) return;
        try
        {
            AppEnumerationService.Launch(app);
        }
        finally
        {
            Close();
        }
    }
}
