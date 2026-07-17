using System.IO;
using System.Net.Http;
using NAudio.Wave;

namespace AiController.Services;

/// <summary>
/// Push-to-talk voice dictation: mic capture -> Groq Whisper -> text
/// injection. Same Groq endpoint and multipart contract as both other
/// platforms (~/ai-controller/scripts/voice_bridge.py on Linux, and
/// ControllerAccessibilityService.kt's transcribeWithGroq on Android) --
/// verified field-for-field against the Android implementation so a Groq
/// account configured for one AI Controller install behaves identically on
/// this one. Everything else here (NAudio capture, HttpClient, the actual
/// injection call) is new, platform-specific code -- only the wire contract
/// with Groq is shared, not any runtime.
///
/// Matches Android's toggle semantics (triggerVoiceDictation: press once to
/// start, press again to stop+transcribe) rather than true hold-to-talk --
/// ControllerInputService only edge-detects button-down, not release, in
/// this pass.
/// </summary>
public sealed class VoiceDictationService : IDisposable
{
    private const string GroqUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string WhisperModel = "whisper-large-v3";

    private readonly HttpClient _http = new();
    private readonly Func<IntPtr> _getTargetWindow;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempPath;
    private bool _isRecording;

    public VoiceDictationService(Func<IntPtr> getTargetWindow)
    {
        _getTargetWindow = getTargetWindow;
    }

    private static string ResolveApiKey()
    {
        var envKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiController");
        var configFile = Path.Combine(configDir, "groq_api_key.txt");
        if (File.Exists(configFile))
        {
            var key = File.ReadAllText(configFile).Trim();
            if (!string.IsNullOrWhiteSpace(key)) return key;
        }

        throw new InvalidOperationException(
            $"Groq API key not configured. Set GROQ_API_KEY or write it to {configFile}.");
    }

    /// <summary>Raised when a dictation attempt fails (missing API key, network error, Groq
    /// error response) so the UI layer can surface it instead of the exception reaching the
    /// caller silently.</summary>
    public event Action<Exception>? DictationFailed;

    /// <summary>
    /// Tap-to-toggle: call on the bound controller input each press. Fires
    /// transcription async on the stop edge.
    ///
    /// async void's exceptions have nowhere to go but the SynchronizationContext
    /// -- unhandled, that crashes the whole WPF app on the very first press
    /// without a configured API key. Microsoft Store policy 10.4.2 requires apps
    /// "remain responsive... handle exceptions... not close unexpectedly," and
    /// a controller button that can crash the app on a missing config value
    /// would fail that on the first real test regardless. Catch here, not just
    /// for certification -- a crash is a bad experience either way.
    /// </summary>
    public async void Toggle()
    {
        try
        {
            if (_isRecording) await StopAndTranscribeAsync();
            else StartRecording();
        }
        catch (Exception ex)
        {
            DictationFailed?.Invoke(ex);
        }
    }

    private void StartRecording()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"ai_controller_ptt_{Guid.NewGuid():N}.wav");
        // 16kHz mono matches what Whisper expects and what the Linux/Android
        // builds already capture at -- no reason to diverge.
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1) };
        _writer = new WaveFileWriter(_tempPath, _waveIn.WaveFormat);
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        _isRecording = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private async Task StopAndTranscribeAsync()
    {
        if (_waveIn == null || _writer == null || _tempPath == null) return;
        _waveIn.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _writer.Dispose();
        _waveIn.Dispose();
        _isRecording = false;

        var wavPath = _tempPath;
        _waveIn = null;
        _writer = null;
        _tempPath = null;

        try
        {
            var transcript = await TranscribeAsync(wavPath);
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                InputInjector.GuardedType(_getTargetWindow(), transcript);
            }
        }
        finally
        {
            try { File.Delete(wavPath); } catch { /* best-effort cleanup */ }
        }
    }

    private async Task<string> TranscribeAsync(string wavPath)
    {
        var apiKey = ResolveApiKey();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(WhisperModel), "model");

        var audioBytes = await File.ReadAllBytesAsync(wavPath);
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(audioContent, "file", "voice_prompt.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, GroqUrl) { Content = form };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Groq API error {(int)response.StatusCode}: {body}");

        using var json = System.Text.Json.JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("text", out var textProp)
            ? textProp.GetString()?.Trim() ?? ""
            : "";
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _writer?.Dispose();
        _http.Dispose();
    }
}
