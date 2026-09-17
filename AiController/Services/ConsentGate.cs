using System.IO;

namespace AiController.Services;

/// <summary>
/// W5 fix: persists whether the operator has consented to sending microphone
/// audio to Groq for transcription. STORE_COMPLIANCE.md flagged this as
/// mandatory and "not built yet" -- Microsoft Store policy 10.5.2 requires
/// opt-in consent, with an explicit description of what's sent and to whom,
/// before a Win32 app transmits a user's personal information (their voice)
/// to a third-party service. App.xaml.cs shows Windows.ConsentGateWindow
/// modally at startup whenever HasConsented() is false, and shuts down if the
/// operator declines -- so no audio can leave the machine without an explicit,
/// persisted yes.
/// </summary>
public static class ConsentGate
{
    // Bump this if the consent prompt's wording changes materially enough that
    // a prior yes shouldn't count -- previously-consented installs get asked again.
    private const string ConsentVersion = "1";

    public static bool HasConsented()
    {
        try
        {
            return File.Exists(AppPaths.ConsentPath) &&
                   File.ReadAllText(AppPaths.ConsentPath).Trim() == ConsentVersion;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void GrantConsent()
    {
        AppPaths.EnsureExists();
        File.WriteAllText(AppPaths.ConsentPath, ConsentVersion);
    }

    public static void RevokeConsent()
    {
        try { File.Delete(AppPaths.ConsentPath); }
        catch (Exception) { /* best-effort -- a failed delete just re-prompts next launch */ }
    }
}
