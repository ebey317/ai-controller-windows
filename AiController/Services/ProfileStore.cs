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
///
/// W1 fix: ButtonAction is now a polymorphic hierarchy (see ControllerModels.cs)
/// instead of a flat enum+string record, so a saved KeyAction/MouseAction/
/// LaunchAction round-trips with all of its fields intact instead of losing
/// everything but a bare type tag. System.Text.Json resolves the concrete
/// subtype from the [JsonDerivedType] "type" discriminator automatically --
/// no hand-rolled converter needed.
///
/// Profiles are named (see ContextSwitcher: "desktop", "browser", "iptv") so
/// each context keeps its own independent mapping.
/// </summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ControllerProfile Load(string name = "profile")
    {
        try
        {
            var path = AppPaths.ProfilePath(name);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var mappings = JsonSerializer.Deserialize<Dictionary<ControllerInput, ButtonAction>>(json, JsonOptions);
                if (mappings != null)
                {
                    var profile = new ControllerProfile { Name = name };
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
        return new ControllerProfile { Name = name };
    }

    public static void Save(ControllerProfile profile, string name = "profile")
    {
        AppPaths.EnsureExists();
        var json = JsonSerializer.Serialize(profile.Mappings, JsonOptions);
        File.WriteAllText(AppPaths.ProfilePath(name), json);
    }
}
