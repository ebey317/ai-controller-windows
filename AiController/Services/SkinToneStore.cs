using System.IO;

namespace AiController.Services;

/// <summary>
/// Persists the customer's emoji skin-tone choice under %APPDATA%\AI Controller
/// (skin_tone). Default is Dark -- the Linux build's signature look; every
/// customer can switch in Settings at any time and it applies immediately
/// (TextStyles.SetSkinTone rebuilds the emoji tables).
/// </summary>
public static class SkinToneStore
{
    public static EmojiSkinTone Load()
    {
        try
        {
            if (File.Exists(AppPaths.SkinTonePath)
                && Enum.TryParse<EmojiSkinTone>(File.ReadAllText(AppPaths.SkinTonePath).Trim(),
                    ignoreCase: true, out var tone))
                return tone;
        }
        catch (IOException) { /* fall back to the default */ }
        return EmojiSkinTone.Dark;
    }

    public static void Save(EmojiSkinTone tone)
    {
        try
        {
            AppPaths.EnsureExists();
            File.WriteAllText(AppPaths.SkinTonePath, tone.ToString());
        }
        catch (IOException) { /* best-effort: tone just won't persist */ }
    }
}