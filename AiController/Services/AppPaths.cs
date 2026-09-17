using System.IO;

namespace AiController.Services;

/// <summary>
/// Centralizes the %APPDATA% config directory so every file that reads or writes
/// app state agrees on where it lives. Deliberately "AI Controller" (with the
/// space) -- that's this app's product/display name, distinct from the
/// "AiController" assembly/namespace identifier used in code. All config, the
/// Groq key, the consent record, profiles, and keyboard state live under here;
/// nothing in this app ever builds a config path with raw string concatenation
/// or a hand-rolled %USERPROFILE%\.config-style path.
/// </summary>
public static class AppPaths
{
    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI Controller");

    public static string ConsentPath => Path.Combine(ConfigDir, "consent.dat");
    public static string GroqApiKeyPath => Path.Combine(ConfigDir, "groq_api_key.txt");
    public static string PinnedSnippetsPath => Path.Combine(ConfigDir, "pinned_snippets.json");
    public static string PttModePath => Path.Combine(ConfigDir, "ptt_mode");
    public static string SkinTonePath => Path.Combine(ConfigDir, "skin_tone");
    public static string LogsDir => Path.Combine(ConfigDir, "logs");

    public static string ProfilePath(string name) => Path.Combine(ConfigDir, $"profile.{name}.json");

    public static void EnsureExists() => Directory.CreateDirectory(ConfigDir);
}
