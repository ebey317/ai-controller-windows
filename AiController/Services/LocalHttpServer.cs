using System.IO;
using System.Net;
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
/// </summary>
public sealed class LocalHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<byte[], CancellationToken, Task<string>> _transcribe;
    private readonly VoiceManager _voiceManager;
    private CancellationTokenSource? _cts;

    public LocalHttpServer(Func<byte[], CancellationToken, Task<string>> transcribe, VoiceManager voiceManager, int port = 7741)
    {
        _transcribe = transcribe;
        _voiceManager = voiceManager;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
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
