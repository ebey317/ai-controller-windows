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
/// Supports both this app's original toggle gesture (ToggleAsync: press once
/// to start, press again to stop+transcribe) and true hold-to-talk
/// (StartAsync/StopAndTranscribeAsync called directly by PttController on the
/// controller's button-down/button-up edges).
///
/// W3 fix: Toggle() used to be `async void` with StartRecording() (a plain
/// synchronous method) called from inside its try block -- but async void's
/// exceptions have nowhere to go but the SynchronizationContext, and more
/// subtly, if NAudio's WaveInEvent/WaveFileWriter construction throws (no mic,
/// device already in use, etc.) partway through, _isRecording could be left
/// unset while _waveIn/_writer were partially constructed, leaving the service
/// stuck. Toggle is now ToggleAsync (an actual async Task, awaitable and with
/// its exceptions observable), StartAsync wraps the NAudio setup in try/catch
/// and tears down any partial state on failure, and a CancellationTokenSource
/// cancels an in-flight transcription if a new recording starts before the
/// previous one's network round-trip finished -- replacing what would
/// otherwise need a hard Thread.Abort to interrupt.
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
    private CancellationTokenSource? _transcribeCts;

    public VoiceDictationService(Func<IntPtr> getTargetWindow)
    {
        _getTargetWindow = getTargetWindow;
    }

    private static string ResolveApiKey()
    {
        var envKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        if (File.Exists(AppPaths.GroqApiKeyPath))
        {
            var key = File.ReadAllText(AppPaths.GroqApiKeyPath).Trim();
            if (!string.IsNullOrWhiteSpace(key)) return key;
        }

        throw new InvalidOperationException(
            $"Groq API key not configured. Set GROQ_API_KEY or write it to {AppPaths.GroqApiKeyPath}.");
    }

    /// <summary>Raised when a dictation attempt fails (missing API key, network error, Groq
    /// error response, or a mic/device failure) so the UI layer can surface it instead of
    /// the exception reaching the caller silently.</summary>
    public event Action<Exception>? DictationFailed;

    /// <summary>Tap-to-toggle: call on the bound controller input each press. Fires
    /// transcription on the stop edge. Exceptions are caught and raised via
    /// DictationFailed rather than propagating -- a controller button press must never
    /// crash the app over a missing config value or a busy mic.</summary>
    public async Task ToggleAsync()
    {
        try
        {
            if (_isRecording) await StopAndTranscribeAsync();
            else await StartAsync();
        }
        catch (Exception ex)
        {
            DictationFailed?.Invoke(ex);
        }
    }

    /// <summary>Begin hold-to-talk capture. Safe to call even if NAudio init fails --
    /// any partial state is torn down and _isRecording is left false so a retry starts
    /// clean instead of getting stuck thinking it's already recording.</summary>
    public async Task StartAsync()
    {
        if (_isRecording) return;

        _tempPath = Path.Combine(Path.GetTempPath(), $"ai_controller_ptt_{Guid.NewGuid():N}.wav");
        try
        {
            // 16kHz mono matches what Whisper expects and what the Linux/Android
            // builds already capture at -- no reason to diverge.
            _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1) };
            _writer = new WaveFileWriter(_tempPath, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            _isRecording = true;
        }
        catch (Exception)
        {
            _waveIn?.Dispose();
            _writer?.Dispose();
            _waveIn = null;
            _writer = null;
            _isRecording = false;
            try { if (_tempPath != null && File.Exists(_tempPath)) File.Delete(_tempPath); }
            catch (IOException) { /* best-effort cleanup of a half-written temp file */ }
            _tempPath = null;
            throw;
        }

        await Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    public async Task StopAndTranscribeAsync()
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

        // Cancel any still-in-flight transcription from a previous take before
        // starting this one -- the closest equivalent of Thread.Abort without
        // actually aborting a thread mid-operation.
        _transcribeCts?.Cancel();
        _transcribeCts = new CancellationTokenSource();

        try
        {
            var audioBytes = await File.ReadAllBytesAsync(wavPath);
            var transcript = await TranscribeAsync(audioBytes, _transcribeCts.Token);
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                // ptt_pynput.py parity: apply the active style mode to the raw
                // transcript before typing it (_transform_text). PRO is a no-op.
                var mode = LoadActiveMode();
                var styled = TextStyles.Apply(transcript, mode);
                InputInjector.GuardedType(_getTargetWindow(), styled);
            }
        }
        finally
        {
            try { File.Delete(wavPath); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    /// <summary>Reads the mode the SlideKeyboard toggled, same file ptt_pynput.py's
    /// MODE_FILE expects (lowercase 'pro'/'bubbly'/'casual'/'bold'/'big').</summary>
    private static TextStyleMode LoadActiveMode()
    {
        try
        {
            if (File.Exists(AppPaths.PttModePath))
            {
                var raw = File.ReadAllText(AppPaths.PttModePath).Trim();
                if (Enum.TryParse<TextStyleMode>(raw, ignoreCase: true, out var mode)) return mode;
            }
        }
        catch (IOException) { /* fall back to PRO */ }
        return TextStyleMode.Pro;
    }

    /// <summary>Transcribe raw WAV bytes via Groq Whisper. Public so LocalHttpServer's
    /// /voice endpoint can reuse the same Groq call for externally-supplied audio.</summary>
    public Task<string> TranscribeBytesAsync(byte[] wavBytes, CancellationToken ct) =>
        TranscribeAsync(wavBytes, ct);

    private async Task<string> TranscribeAsync(byte[] audioBytes, CancellationToken ct)
    {
        var apiKey = ResolveApiKey();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(WhisperModel), "model");

        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(audioContent, "file", "voice_prompt.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, GroqUrl) { Content = form };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Groq API error {(int)response.StatusCode}: {body}");

        using var json = System.Text.Json.JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("text", out var textProp)
            ? textProp.GetString()?.Trim() ?? ""
            : "";
    }

    public void Dispose()
    {
        _transcribeCts?.Cancel();
        _waveIn?.Dispose();
        _writer?.Dispose();
        _http.Dispose();
    }
}
