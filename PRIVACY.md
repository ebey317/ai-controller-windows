# Privacy Policy — AI Controller (Windows)

This document describes what data AI Controller collects, sends, and stores,
and why. It exists to satisfy Microsoft Store policy 10.5.1 (a privacy policy
is mandatory for any app accessing personal information) and 10.5.2 (opt-in
consent, with an explicit description of what's sent and to whom, before
transmitting personal information to a third party) — see
`STORE_COMPLIANCE.md` for the full policy research this app was built
against.

## What this app can access

- **Your game controller's button/trigger/stick state** (via XInput), to
  drive its input-mapping features. This never leaves your machine.
- **Your microphone**, only while you are actively using voice dictation
  (pressing/holding the button bound to a Voice action). The app does not
  record continuously and does not listen in the background.
- **The Start Menu's shortcut files**, to power the app launcher. This never
  leaves your machine.
- **The foreground window's process name**, to power automatic profile
  switching between desktop/browser/IPTV contexts. This never leaves your
  machine.

## What gets sent to a third party, and to whom

When you use voice dictation, the audio you record **and your Groq API key**
are sent to **Groq** (`api.groq.com`), a third-party speech-to-text provider,
for transcription via their Whisper API. Your API key is sent as a standard
Bearer-token Authorization header on every transcription request — the same
way any Groq API client authenticates. Audio and your API key are the only
data this app sends off your machine, and this only happens while you are
actively dictating.

- Audio is sent using your own Groq API key, which you provide in Settings.
  This app never ships or embeds a shared key.
- The temporary WAV file used for capture is written to your OS temp
  directory (e.g. `%TEMP%`) — not this app's `%APPDATA%` folder — and is
  deleted on a best-effort basis immediately after the request completes (or
  fails). That deletion can fail (for example if the file is still locked),
  and in rare cases the file can be left behind if the app is closed mid-
  capture; this app does not guarantee the temp file is always removed.
- No audio, transcript, or usage data is sent to the developer of this app,
  or to any service other than Groq.
- What Groq itself does with the audio it receives is governed by
  [Groq's own privacy policy](https://groq.com/privacy-policy/) — this app
  has no control over Groq's retention or use of that data once sent.

## Consent

The first time you launch the app, it shows a consent screen describing the
above before any voice-dictation code path can run. Declining exits the app
immediately without recording anything. Your choice is persisted to
`%APPDATA%\AI Controller\consent.dat`; delete that file (or use a future
Settings toggle) to be asked again.

## Local network bridge

The app runs a small HTTP server on `127.0.0.1:7741` (localhost only — not
reachable from any other device) that exposes `/voice` (transcribe uploaded
audio) and `/speak` (speak text aloud). Both endpoints require a random
bearer token generated fresh each time the app starts, sent as an
`Authorization: Bearer <token>` header — only local clients that already have
that token can use them. Nothing outside your machine can ever reach this
server, and even another process on the same machine cannot use it without
that token; it exists so other authenticated local tools you run can use the
same voice pipeline this app uses for its own controller-driven dictation.

## What is stored on disk, and where

Everything this app persists lives under `%APPDATA%\AI Controller\`:

- `groq_api_key.txt` — your Groq API key, in plain text, exactly as you
  entered it in Settings.
- `consent.dat` — whether you've accepted the voice-dictation consent prompt.
- `profile.<context>.json` — your button mappings per context (desktop,
  browser, IPTV).
- `pinned_snippets.json` — the slide keyboard's 5 pinned snippet slots.
- `ptt_mode` — the slide keyboard's currently selected text style.

None of this is sent anywhere except the Groq API key, which is sent only to
Groq, only as part of a transcription request you initiated.

## Accessibility use

This app reads controller input and synthesizes keyboard/mouse input via
documented, public Win32 APIs (`XInputGetState`, `SendInput`) for the purpose
of accessible input control — the same class of legitimate use as existing
macro, remote-control, and assistive-technology tools already distributed
through the Microsoft Store. It does not use undocumented APIs, and does not
modify, inspect, or exfiltrate the content of other applications beyond
reading the currently-focused window handle needed to target injected input.

## Contact / revoking consent

To revoke voice-dictation consent, delete
`%APPDATA%\AI Controller\consent.dat` — you'll be asked again on next launch.
To stop the app from accessing your microphone entirely, remove any Voice
binding in Settings.
