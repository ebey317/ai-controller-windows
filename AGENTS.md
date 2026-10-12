# AGENTS.md

Guidance for AI coding agents working in this repo. Read this before touching anything under `AiController/`.

## What this project is

AI Controller for Windows is the C#/WPF/.NET 8 port of the Linux `ai-controller` accessibility system (source of truth: `~/refactor-staging/ai-controller`, daily-used, Python). It turns a wired Xbox Series X/S controller into a full keyboard/mouse/voice input device so someone with limited mobility can run their whole desktop without touching a physical keyboard or mouse.

This is a **port**, not a fork of the Linux runtime — there is no shared code or process between the two. The goal is module-for-module parity: same module names, same data flow, same config conventions, reimplemented natively for Win32/WPF instead of X11/GTK/systemd. When in doubt about intended behavior, read the corresponding Linux script for the *reasoning*, then reimplement it the Windows-native way — don't try to share code, and don't assume an X11/GTK idiom (window managers, `xdotool`, `pynput`, systemd units) has a direct equivalent here.

## Components (Linux script → Windows equivalent)

| Linux (source of truth) | Windows | Purpose |
|---|---|---|
| AntiMicroX + uinput | `Services/ControllerInputService.cs` | XInput polling, button edge detection |
| `scripts/ptt_pynput.py` | `Services/PttController.cs` + `Services/VoiceDictationService.cs` | Push-to-talk state machine, mic capture → Groq Whisper |
| `scripts/voice_bridge.py` | `Services/LocalHttpServer.cs` (127.0.0.1:7741) | Local `/voice` + `/speak` HTTP bridge |
| `scripts/voice_manager.py` | `Services/VoiceManager.cs` | TTS backend toggle (System.Speech first cut; Edge-TTS-style backend is a placeholder) |
| `scripts/text_styles.py` | `Services/TextStyles.cs` | PRO/BUBBLY/CASUAL/BOLD/BIG Unicode style transforms |
| `scripts/slide_keyboard.py` | `Windows/SlideKeyboard.xaml(.cs)` | On-screen keyboard with mode chips + 5 pinned snippet slots |
| (plain grid keyboard concept) | `Windows/OnScreenKeyboardWindow.xaml(.cs)` | Plain grid on-screen keyboard |
| `scripts/controller-legend.py` | `Windows/LegendOverlay.xaml(.cs)` | HUD strip showing the active profile's button legend, follows the cursor |
| `scripts/controller-profile-switcher.sh` | `Services/ContextSwitcher.cs` | Swaps the active `ControllerProfile` by foreground-window process (desktop/browser/IPTV) |
| `scripts/settle_wiggle.sh` | `Services/DriftCalibrator.cs` | Pre-click cursor settle nudge + analog-stick rest-drift correction |
| rofi (`ai-rofi-launcher.sh`) | `Windows/LauncherWindow.xaml(.cs)` + `Services/AppEnumerationService.cs` | App search/launch |
| — (Windows-only) | `Windows/ConsentGateWindow.xaml(.cs)` + `Services/ConsentGate.cs` | Opt-in consent gate before the first Groq call (Store policy 10.5.2) |

## Hard rules for agents

- **Never edit the Linux repo from here.** `~/refactor-staging/ai-controller` is the daily-driver, source-of-truth install. This repo only reads it for reference.
- **No Linux/WSL idioms.** No `xdotool`, no X11 window-class matching, no systemd units, no `~/.config/...` paths. Controller input is XInput (P/Invoke), synthesis is `SendInput` (`Services/InputInjector.cs`), config lives under `%APPDATA%\AI Controller\` via `Services/AppPaths.cs`.
- **All config paths go through `AppPaths`**, never a hand-rolled `Path.Combine(...)` scattered across files, and never raw string concatenation.
- **`ButtonAction` is a polymorphic hierarchy** (`Models/ControllerModels.cs`), not a flat enum. If you add a new action kind, add a new `ButtonAction` subclass with a `[JsonDerivedType]` entry — don't bolt another optional field onto an existing subclass.
- **Windows that must never steal focus** (on-screen keyboards, the legend HUD) go through `Services/NativeWindowHelper.SetNoActivate` in their `SourceInitialized` handler, and never set `Owner`. See that file's doc comment for why.
- **Never ship a Groq API key.** It's read from `GROQ_API_KEY` or `%APPDATA%\AI Controller\groq_api_key.txt`, entered via Settings. Never hardcode one, never commit one.
- **The Groq consent gate is not optional.** Don't add a code path that can transcribe audio via Groq before `ConsentGate.HasConsented()` is true.

## Making a safe change

1. Check the table above for the Linux equivalent before designing a fix — the *behavior* should match; the *implementation* almost never can, and shouldn't try to.
2. `dotnet build -c Release` from the repo root before considering a change done.
3. If you touch the input pipeline (`ControllerInputService`, `InputInjector`, `PttController`, `VoiceDictationService`), reason explicitly about the WPF dispatcher thread: `ControllerInputService.Poll` runs on a `System.Threading.Timer` callback thread, not the UI thread — anything touching a `Window` from that callback must go through `Dispatcher.Invoke`.
4. Keep `git status` clean — no stray `bin/`, `obj/`, or `.user` files staged (already gitignored; don't force-add them).

## Project status

Phase 2 (this pass): the five critical bugs from the initial C# port are fixed (polymorphic `ButtonAction`, keyboard-window focus-stealing, async voice dictation, button-release detection, the consent gate), and the framework-parity modules listed above are in place. See `README.md` for build/run instructions and `PRIVACY.md` for the consent/data-handling policy.
