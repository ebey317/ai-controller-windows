using System.Speech.Synthesis;

namespace AiController.Services;

/// <summary>Mirrors voice_manager.py's voice-pack abstraction: a swappable TTS
/// backend rather than one hardwired engine. Linux toggles between Piper (offline)
/// and edge_tts (networked, higher fidelity); the Windows equivalents are
/// System.Speech (built into Windows, offline -- wired up here as the first cut)
/// and an Edge-TTS-style networked voice (placeholder, not implemented this pass).</summary>
public enum VoiceBackend { SystemSpeech, EdgeTts }

/// <summary>
/// Text-to-speech for LocalHttpServer's /speak endpoint and any future
/// assistant-response playback. Only VoiceBackend.SystemSpeech is implemented;
/// EdgeTts exists so the backend toggle has the same two-option shape as
/// voice_manager.py's, without silently dropping an utterance if it's selected
/// before that backend is built out.
/// </summary>
public sealed class VoiceManager : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public VoiceBackend Backend { get; set; } = VoiceBackend.SystemSpeech;

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (Backend == VoiceBackend.EdgeTts)
        {
            // Not implemented in this pass -- fall back to the offline backend
            // rather than silently dropping the utterance.
            SpeakWithSystemSpeech(text);
            return;
        }

        SpeakWithSystemSpeech(text);
    }

    private void SpeakWithSystemSpeech(string text)
    {
        _synth.SpeakAsyncCancelAll();
        _synth.SpeakAsync(text);
    }

    public void Stop() => _synth.SpeakAsyncCancelAll();

    public void Dispose() => _synth.Dispose();
}
