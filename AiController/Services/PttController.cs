using AiController.Models;

namespace AiController.Services;

/// <summary>
/// Push-to-talk state machine -- the Windows equivalent of ptt_pynput.py's
/// start_recording()/stop_and_send() pair. That script drives PTT off a
/// physical F13 keypress (remapped from the controller's trigger by
/// AntiMicroX/xmodmap on the Linux build); here the same press/release
/// gesture arrives directly from ControllerInputService's XInput edges, so
/// there's no keyboard-remap layer to go through.
///
/// Owns the two PTT gestures a bound VoiceAction can use:
///   - VoiceMode.PushToTalk: OnPress starts capture, OnRelease stops and
///     transcribes -- true hold-to-talk, mirroring ptt_pynput.py's model.
///   - VoiceMode.Toggle: OnPress alone starts OR stops+transcribes (this
///     app's original press-once/press-again behavior); OnRelease is a no-op
///     for this mode since the gesture doesn't use a release edge at all.
///
/// Debounces presses the same way ptt_pynput.py's _DEBOUNCE_MS does --
/// controller trigger chatter (a single physical pull briefly bouncing across
/// the analog threshold) must not be read as two separate presses.
/// </summary>
public sealed class PttController
{
    private const int DebounceMs = 250;

    // F5 fix: a release arriving before the trigger has been held this long is
    // controller chatter, not an intentional tap -- reusing DebounceMs since
    // that's already the threshold this class uses to distinguish a deliberate
    // gesture from bounce on the same input.
    private const int MinHoldMs = DebounceMs;

    private readonly VoiceDictationService _voice;
    private readonly Action<Exception>? _onError;
    private long _lastPressTicks = long.MinValue;
    private long _holdStartTicks;
    private bool _isHeld;

    public PttController(VoiceDictationService voice, Action<Exception>? onError = null)
    {
        _voice = voice;
        _onError = onError;
    }

    public void OnPress(VoiceMode mode)
    {
        var now = Environment.TickCount64;
        if (now - _lastPressTicks < DebounceMs) return;
        _lastPressTicks = now;

        _ = RunAsync(async () =>
        {
            if (mode == VoiceMode.PushToTalk)
            {
                _isHeld = true;
                _holdStartTicks = now;
                await _voice.StartAsync();
            }
            else
            {
                await _voice.ToggleAsync();
            }
        });
    }

    public void OnRelease(VoiceMode mode)
    {
        if (mode != VoiceMode.PushToTalk || !_isHeld) return;

        // A release this soon after the press is chatter, not a deliberate
        // tap-and-release -- ignore it entirely: don't transcribe, and don't
        // clear _isHeld, so the eventual real release still fires normally
        // instead of this bogus one consuming the held state.
        if (Environment.TickCount64 - _holdStartTicks < MinHoldMs) return;

        _isHeld = false;
        _ = RunAsync(() => _voice.StopAndTranscribeAsync());
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }
}
