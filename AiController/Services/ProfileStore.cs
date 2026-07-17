using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiController.Models;

namespace AiController.Services;

/// <summary>
/// JSON persistence for ControllerProfile -- mirrors the Android app's
/// ProfileManager.kt (JSON-serialized ControllerProfile persisted to
/// SharedPreferences). Same idea, different storage mechanism: a plain
/// file under %APPDATA% instead of Android's key-value store, since a
/// desktop app has no equivalent of SharedPreferences.
/// </summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiController");

    private static string ProfilePath => Path.Combine(ConfigDir, "profile.json");

    public static ControllerProfile Load()
    {
        try
        {
            if (File.Exists(ProfilePath))
            {
                var json = File.ReadAllText(ProfilePath);
                var mappings = JsonSerializer.Deserialize<Dictionary<ControllerInput, ButtonAction>>(json, JsonOptions);
                if (mappings != null)
                {
                    var profile = new ControllerProfile();
                    profile.Mappings.Clear();
                    foreach (var (key, value) in mappings) profile.Mappings[key] = value;
                    return profile;
                }
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable profile -- fall back to defaults rather
            // than crash the app over a settings file.
        }
        return new ControllerProfile();
    }

    public static void Save(ControllerProfile profile)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(profile.Mappings, JsonOptions);
        File.WriteAllText(ProfilePath, json);
    }
}
