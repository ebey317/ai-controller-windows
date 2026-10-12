# AI Controller (Windows)

Turn a wired Xbox Series X/S controller into a full keyboard/mouse/voice input
device for Windows — an accessibility tool for anyone who wants (or needs) to
run their whole desktop without touching a physical keyboard or mouse.

This is the C#/WPF/.NET 8 port of the Linux `ai-controller` project. See
[`AGENTS.md`](AGENTS.md) for the full module-parity map and
[`PRIVACY.md`](PRIVACY.md) for what data leaves your machine and when.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A wired Xbox controller (XInput-compatible)
- A [Groq](https://console.groq.com/) API key, if you want voice dictation

## Build

```powershell
cd ai-controller-windows-work
dotnet build -c Release
```

## Run

```powershell
dotnet run --project AiController -c Release
```

The first launch shows a one-time consent screen before any microphone audio
can be sent anywhere (see [`PRIVACY.md`](PRIVACY.md)). After that, the app
lives in the system tray — right-click the tray icon for **Settings**.

## Configuring your controller

Open **Settings** (tray icon → Settings) to:

- Enter your Groq API key (for voice dictation). It's stored at
  `%APPDATA%\AI Controller\groq_api_key.txt` — never committed anywhere, never
  baked into the app.
- Toggle "start on login."
- Remap each button to an action: **Key** (type a key name, e.g. `Enter`,
  `Tab`, `A`), **Mouse** (click), **Voice** (push-to-talk / dictation
  toggle), **Keyboard** (opens the on-screen keyboard), or **Launch** (opens
  the app launcher, or a named app if you enter one).

## Default bindings

| Button | Action |
|---|---|
| A | Left click |
| Y | App launcher |
| Right Trigger | Voice dictation (press to start, press again to stop + transcribe) |
| Back / View | On-screen keyboard |

Everything else is unbound by default — configure it in Settings.

## On-screen keyboards

- **Plain grid keyboard** — a floating QWERTY grid; types directly into
  whatever window had focus when it was opened.
- **Slide keyboard** — the same grid plus a mode bar (PRO / BUBBLY / CASUAL /
  BOLD / BIG text styles) and 5 pinned snippet slots for quick-insert text.
  Toggle "Edit Pins" and tap a slot to set its text using the same on-screen
  grid — no physical keyboard required for setup either.

Neither keyboard ever steals foreground focus from the window you're typing
into (see `AGENTS.md`'s note on `NativeWindowHelper`).

## Context-aware profiles

The app keeps three independent button-mapping profiles — **desktop**,
**browser**, and **IPTV** — and automatically switches between them based on
which app is in the foreground (`Services/ContextSwitcher.cs`). Edit each
context's profile file directly under `%APPDATA%\AI Controller\` if you want
different bindings per context; Settings currently edits the desktop profile.

## Local voice bridge

A localhost-only HTTP server on `127.0.0.1:7741` exposes:

- `POST /voice` — upload WAV audio, get back `{"text": "..."}` (Groq Whisper transcription).
- `POST /speak` — POST `text=...`, hear it spoken back through the configured TTS backend.

Bound to `127.0.0.1` only — nothing outside this machine can ever reach it.

## Development

See [`AGENTS.md`](AGENTS.md) for the module-parity map against the Linux
source of truth, and the coding conventions this repo follows.
