using System.Windows;
using AiController.Models;
using AiController.Services;
using AiController.Windows;

namespace AiController;

/// <summary>
/// Phase 2 entry point: adds the launcher, on-screen keyboard, settings
/// window, tray icon, and autostart on top of Phase 1's core input
/// pipeline (controller reading, injection, voice dictation).
/// </summary>
public partial class App : System.Windows.Application
{
    private ControllerInputService? _controllerService;
    private VoiceDictationService? _voiceService;
    private TrayIconHost? _trayIcon;
    private OnScreenKeyboardWindow? _keyboardWindow;
    private LauncherWindow? _launcherWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var profile = ProfileStore.Load();
        _voiceService = new VoiceDictationService(() => InputInjector.ActiveWindow());
        _voiceService.DictationFailed += ex => System.Diagnostics.Debug.WriteLine($"Dictation failed: {ex.Message}");

        _keyboardWindow = new OnScreenKeyboardWindow();
        _trayIcon = new TrayIconHost(profile);

        _controllerService = new ControllerInputService(profile, action =>
        {
            // ControllerInputService.Poll runs on a System.Threading.Timer
            // callback thread, not the WPF dispatcher thread -- every action
            // here that touches a Window has to be marshalled back via
            // Dispatcher.Invoke, or WPF throws on the cross-thread access.
            Dispatcher.Invoke(() =>
            {
                switch (action.Type)
                {
                    case ActionType.VoiceTrigger:
                        _voiceService.Toggle();
                        break;
                    case ActionType.MouseClick:
                        InputInjector.GuardedClick(InputInjector.ActiveWindow());
                        break;
                    case ActionType.ShowKeyboard:
                        _keyboardWindow!.Toggle();
                        break;
                    case ActionType.LaunchApp:
                        if (_launcherWindow == null || !_launcherWindow.IsVisible)
                        {
                            _launcherWindow = new LauncherWindow();
                            _launcherWindow.Show();
                            _launcherWindow.Activate();
                        }
                        break;
                }
            });
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controllerService?.Dispose();
        _voiceService?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
