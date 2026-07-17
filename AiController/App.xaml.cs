using System.Windows;
using AiController.Models;
using AiController.Services;

namespace AiController;

/// <summary>
/// Phase 1 entry point: wires ControllerInputService to VoiceDictationService
/// and InputInjector, no UI yet. LauncherWindow, OnScreenKeyboardWindow,
/// SettingsWindow, a real tray icon, and autostart are Phase 2 -- deferred so
/// this pass can focus on proving the core input pipeline (the actual
/// technical risk) rather than UI that has no runtime to verify from here
/// anyway. See the plan for the phase boundary.
/// </summary>
public partial class App : Application
{
    private ControllerInputService? _controllerService;
    private VoiceDictationService? _voiceService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var profile = new ControllerProfile();
        _voiceService = new VoiceDictationService(() => InputInjector.ActiveWindow());
        // Phase 2: surface this in the UI (a toast/tray balloon) instead of a
        // debug write -- for now, just guarantee it's never silently dropped.
        _voiceService.DictationFailed += ex => System.Diagnostics.Debug.WriteLine($"Dictation failed: {ex.Message}");

        _controllerService = new ControllerInputService(profile, action =>
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
                case ActionType.LaunchApp:
                    // Phase 2: OnScreenKeyboardWindow / LauncherWindow don't exist yet.
                    break;
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controllerService?.Dispose();
        _voiceService?.Dispose();
        base.OnExit(e);
    }
}
