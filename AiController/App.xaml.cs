using System.Windows;
using AiController.Models;
using AiController.Services;
using AiController.Windows;

namespace AiController;

/// <summary>
/// Entry point: consent gate, per-context profiles, the core input pipeline
/// (controller reading, injection, voice dictation), both on-screen keyboards,
/// the legend HUD, the local voice HTTP bridge, tray icon, and autostart.
/// </summary>
public partial class App : System.Windows.Application
{
    private ControllerInputService? _controllerService;
    private VoiceDictationService? _voiceService;
    private VoiceManager? _voiceManager;
    private PttController? _pttController;
    private LocalHttpServer? _httpServer;
    private ContextSwitcher? _contextSwitcher;
    private TrayIconHost? _trayIcon;
    private OnScreenKeyboardWindow? _keyboardWindow;
    private SlideKeyboard? _slideKeyboardWindow;
    private LegendOverlay? _legendOverlay;
    private LauncherWindow? _launcherWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // W5: no Groq call may happen before the operator has explicitly opted in.
        if (!ConsentGate.HasConsented())
        {
            var gate = new ConsentGateWindow();
            gate.ShowDialog();
            if (!gate.Accepted)
            {
                Shutdown();
                return;
            }
        }

        // Apply the customer's saved emoji skin tone before anything can type.
        TextStyles.SetSkinTone(SkinToneStore.Load());

        var desktopProfile = ProfileStore.Load("desktop");
        var browserProfile = ProfileStore.Load("browser");
        var iptvProfile = ProfileStore.Load("iptv");

        _voiceManager = new VoiceManager();
        _voiceService = new VoiceDictationService(() => InputInjector.ActiveWindow());
        _voiceService.DictationFailed += ex => System.Diagnostics.Debug.WriteLine($"Dictation failed: {ex.Message}");
        _pttController = new PttController(_voiceService,
            onError: ex => System.Diagnostics.Debug.WriteLine($"PTT failed: {ex.Message}"));

        _httpServer = new LocalHttpServer(_voiceService.TranscribeBytesAsync, _voiceManager);
        _httpServer.Start();

        _keyboardWindow = new OnScreenKeyboardWindow();
        _slideKeyboardWindow = new SlideKeyboard();
        _legendOverlay = new LegendOverlay(desktopProfile);
        _trayIcon = new TrayIconHost(desktopProfile);

        _controllerService = new ControllerInputService(
            desktopProfile,
            onPress: action => Dispatcher.Invoke(() => HandlePress(action)),
            onRelease: action => Dispatcher.Invoke(() => HandleRelease(action)));

        _contextSwitcher = new ContextSwitcher(new()
        {
            [ProfileContext.Desktop] = desktopProfile,
            [ProfileContext.Browser] = browserProfile,
            [ProfileContext.Iptv] = iptvProfile,
        });
        _contextSwitcher.ContextChanged += profile =>
        {
            _controllerService.ActiveProfile = profile;
            _legendOverlay.SetProfile(profile);
        };
    }

    private void HandlePress(ButtonAction action)
    {
        switch (action)
        {
            case VoiceAction voice:
                _pttController!.OnPress(voice.Mode);
                break;

            case MouseAction mouse:
                // settle_wiggle.sh's fix, ported: nudge the cursor before the
                // synthesized click so the window manager recomputes hover state.
                DriftCalibrator.Settle();
                InputInjector.GuardedMouse(InputInjector.ActiveWindow(), mouse.Button, mouse.Kind);
                break;

            case KeyAction key:
                var vk = InputInjector.ResolveVirtualKey(key.KeyText);
                if (vk != 0) InputInjector.GuardedKey(InputInjector.ActiveWindow(), vk);
                break;

            case KeyboardAction keyboard:
                if (keyboard.Mode == KeyboardMode.Slide) _slideKeyboardWindow!.Toggle();
                else _keyboardWindow!.Toggle();
                break;

            case LaunchAction launch:
                HandleLaunch(launch);
                break;
        }
    }

    private void HandleRelease(ButtonAction action)
    {
        if (action is VoiceAction voice) _pttController!.OnRelease(voice.Mode);
    }

    private void HandleLaunch(LaunchAction launch)
    {
        if (!string.IsNullOrWhiteSpace(launch.AppTarget))
        {
            var match = AppEnumerationService.Enumerate()
                .FirstOrDefault(a => a.Name.Equals(launch.AppTarget, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                AppEnumerationService.Launch(match);
                return;
            }
        }

        if (_launcherWindow == null || !_launcherWindow.IsVisible)
        {
            _launcherWindow = new LauncherWindow();
            _launcherWindow.Show();
            _launcherWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controllerService?.Dispose();
        _contextSwitcher?.Dispose();
        _httpServer?.Dispose();
        _voiceService?.Dispose();
        _voiceManager?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
