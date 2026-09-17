using System.Windows;
using AiController.Services;

namespace AiController.Windows;

/// <summary>
/// W5 fix: blocking consent gate shown before the first Groq call.
/// App.xaml.cs shows this modally (ShowDialog) at startup whenever
/// ConsentGate.HasConsented() is false, and shuts the app down entirely if
/// the operator declines -- so no microphone audio can ever leave the machine
/// without an explicit, persisted opt-in (Microsoft Store policy 10.5.2).
/// </summary>
public partial class ConsentGateWindow : Window
{
    public bool Accepted { get; private set; }

    public ConsentGateWindow()
    {
        InitializeComponent();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (!ConsentGate.GrantConsent())
        {
            System.Windows.MessageBox.Show(this,
                "Could not save your consent choice to disk. Check that AI Controller " +
                "can write to %APPDATA%\\AI Controller, then try again.",
                "AI Controller — Voice Privacy",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Accepted = true;
        Close();
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}
