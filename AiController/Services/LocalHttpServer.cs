using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiController.Services;

/// <summary>
/// Localhost-only HTTP server exposing /voice and /speak -- the Windows
/// equivalent of voice_bridge.py's FastAPI app. Bound to 127.0.0.1 only (not
/// 0.0.0.0), which is this app's whole security boundary for these routes:
/// nothing outside the machine can ever reach them, mirroring voice_bridge.py's
/// explicit _local_only guard even though here the bind address does that job
/// by construction.
///
/// Runs on :7741, not Linux's :8002 -- deliberately different so a Windows
/// install and a Linux install of this app reachable on the same network never
/// collide on the same port.
///
/// Scope: /voice here always behaves like voice_bridge.py's mode=="transcribe_only"
/// path (STT only, returns {"text": ...}) -- there is no LLM round-trip or
/// TTS-of-the-response step, since this app's controller/voice pipeline already
/// owns that decision via VoiceDictationService. /speak matches voice_bridge.py's
/// contract directly: POST text, it gets spoken through VoiceManager.
///
/// F2 fix: binding to 127.0.0.1 keeps other MACHINES out, but it does nothing to
/// stop any other PROCESS on this same machine from hitting /voice and burning
/// the operator's Groq key. A random bearer token is generated fresh every
/// Start(), logged once, and required (constant-time compare) on both routes;
/// requests carrying an Origin header (a browser tab, not a local CLI/script) or
/// targeting a non-loopback Host are rejected before either handler runs.
/// </summary>
public sealed class LocalHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<byte[], CancellationToken, Task<string>> _transcribe;
    private readonly VoiceManager _voiceManager;
    private CancellationTokenSource? _cts;

    /// <summary>Per-run bearer token required on /voice and /speak. Regenerated on
    /// every Start() so a token from a prior run never grants access to this one.
    /// Public so a future UI can display it to the operator.</summary>
    public string AuthToken { get; private set; } = "";

    public LocalHttpServer(Func<byte[], CancellationToken, Task<string>> transcribe, VoiceManager voiceManager, int port = 7741)
    {
        _transcribe = transcribe;
        _voiceManager = voiceManager;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        AuthToken = RandomNumberGenerator.GetHexString(32);
        System.Diagnostics.Debug.WriteLine($"LocalHttpServer: bearer token for this run is {AuthToken}");

        _cts = new CancellationTokenSource();
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            System.Diagnostics.Debug.WriteLine($"LocalHttpServer failed to start: {ex.Message}");
            return;
        }
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested || !_listener.IsListening) return;
                continue;
            }
            _ = Task.Run(() => HandleAsync(ctx), token);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (!IsAuthorized(ctx.Request))
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (path == "/voice" && ctx.Request.HttpMethod == "POST")
                await HandleVoiceAsync(ctx);
            else if (path == "/speak" && ctx.Request.HttpMethod == "POST")
                await HandleSpeakAsync(ctx);
            else
                ctx.Response.StatusCode = 404;
        }
        catch (Exception)
        {
            ctx.Response.StatusCode = 500;
        }
        finally
        {
            ctx.Response.OutputStream.Close();
        }
    }

    /// <summary>Rejects before either handler ever runs: no Origin header (a browser
    /// page, not a trusted local script/CLI), a loopback Host, and a matching bearer
    /// token, compared in constant time so response timing can't leak the token.</summary>
    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (request.Headers["Origin"] != null) return false;
        if (!IsLoopbackHost(request.UserHostName)) return false;

        var authHeader = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            return false;

        var provided = authHeader["Bearer ".Length..];
        return ConstantTimeEquals(provided, AuthToken);
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var hostOnly = host.Split(':')[0];
        return hostOnly is "127.0.0.1" or "localhost" or "::1";
    }

    private static bool ConstantTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private async Task HandleVoiceAsync(HttpListenerContext ctx)
    {
        using var ms = new MemoryStream();
        await ctx.Request.InputStream.CopyToAsync(ms);
        var text = await _transcribe(ms.ToArray(), CancellationToken.None);
        await WriteJsonAsync(ctx, new Dictionary<string, string> { ["text"] = text });
    }

    private async Task HandleSpeakAsync(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        var text = ParseFormField(body, "text") ?? body;
        _voiceManager.Speak(text);
        await WriteJsonAsync(ctx, new Dictionary<string, bool> { ["spoken"] = true });
    }

    /// <summary>Minimal application/x-www-form-urlencoded field lookup -- avoids
    /// pulling in a System.Web/ASP.NET dependency for one form field.</summary>
    private static string? ParseFormField(string body, string field)
    {
        foreach (var pair in body.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == field)
                return Uri.UnescapeDataString(parts[1].Replace("+", " "));
        }
        return null;
    }

    private static async Task WriteJsonAsync<T>(HttpListenerContext ctx, T payload)
    {
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
            // Listener may already be stopped/disposed on a second Dispose -- fine.
        }
    }
}
