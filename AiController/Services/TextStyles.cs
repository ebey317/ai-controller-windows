using System.Text;
using System.Linq;

namespace AiController.Services;

/// <summary>The five transcript/typing styles, exactly matching the Linux build's
/// scripts/text_styles.py font maps and scripts/ptt_pynput.py's mode dispatch
/// (_transform_text). Same five names, same semantics: PRO is unchanged passthrough,
/// BUBBLY is cursive Unicode, CASUAL lowercases, BOLD and BIG map to bold-mathematical
/// and bold-Fraktur ("Old English") Unicode respectively.</summary>
public enum TextStyleMode { Pro, Bubbly, Casual, Bold, Big }

/// <summary>
/// Unicode font-mapping helpers, ported field-for-field from text_styles.py's
/// _CURSIVE_MAP/_BOLD_MAP/_FRAKTUR_MAP, plus ptt_pynput.py's emoji pipeline
/// (keyword-emoji insertion, dark skin-tone modifiers, casual emoji boost) --
/// the complete _transform_text feature set, since voice transcripts are
/// styled here too, not just the on-screen keyboard's mode chips.
/// </summary>
public static class TextStyles
{
    // Mathematical alphanumeric letter blocks (U+1D400+) live outside the Basic
    // Multilingual Plane, so every glyph here is a UTF-16 surrogate pair, not a
    // single char -- these maps go char -> 2-code-unit string, not char -> char.
    private const string CursiveLower = "𝓪𝓫𝓬𝓭𝓮𝓯𝓰𝓱𝓲𝓳𝓴𝓵𝓶𝓷𝓸𝓹𝓺𝓻𝓼𝓽𝓾𝓿𝔀𝔁𝔂𝔃";
    private const string CursiveUpper = "𝓐𝓑𝓒𝓓𝓔𝓕𝓖𝓗𝓘𝓙𝓚𝓛𝓜𝓝𝓞𝓟𝓠𝓡𝓢𝓣𝓤𝓥𝓦𝓧𝓨𝓩";
    private const string BoldLower = "𝐚𝐛𝐜𝐝𝐞𝐟𝐠𝐡𝐢𝐣𝐤𝐥𝐦𝐧𝐨𝐩𝐪𝐫𝐬𝐭𝐮𝐯𝐰𝐱𝐲𝐳";
    private const string BoldUpper = "𝐀𝐁𝐂𝐃𝐄𝐅𝐆𝐇𝐈𝐉𝐊𝐋𝐌𝐍𝐎𝐏𝐐𝐑𝐒𝐓𝐔𝐕𝐖𝐗𝐘𝐙";

    // Bold Fraktur (U+1D56C), not plain Fraktur (U+1D504): plain Fraktur has legacy
    // gaps (missing C/H/I/R/Z, aliased to pre-existing Letterlike Symbols). Bold
    // Fraktur is a contiguous, fully populated 52-codepoint block -- same reasoning
    // as the Linux build's comment on this exact choice.
    private const string FrakturLower = "𝖆𝖇𝖈𝖉𝖊𝖋𝖌𝖍𝖎𝖏𝖐𝖑𝖒𝖓𝖔𝖕𝖖𝖗𝖘𝖙𝖚𝖛𝖜𝖝𝖞𝖟";
    private const string FrakturUpper = "𝕬𝕭𝕮𝕯𝕰𝕱𝕲𝕳𝕴𝕵𝕶𝕷𝕸𝕹𝕺𝕻𝕼𝕽𝕾𝕿𝖀𝖁𝖂𝖃𝖄𝖅";

    private static readonly Dictionary<char, string> CursiveMap = BuildMap(CursiveLower, CursiveUpper);
    private static readonly Dictionary<char, string> BoldMap = BuildMap(BoldLower, BoldUpper);
    private static readonly Dictionary<char, string> FrakturMap = BuildMap(FrakturLower, FrakturUpper);

    private static Dictionary<char, string> BuildMap(string lower, string upper)
    {
        var map = new Dictionary<char, string>();
        for (var i = 0; i < 26; i++)
        {
            map[(char)('a' + i)] = lower.Substring(i * 2, 2);
            map[(char)('A' + i)] = upper.Substring(i * 2, 2);
        }
        return map;
    }

    private static string MapText(string text, Dictionary<char, string> map)
    {
        var sb = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
            sb.Append(map.TryGetValue(ch, out var mapped) ? mapped : ch.ToString());
        return sb.ToString();
    }

    public static string ToCursive(string text) => MapText(text, CursiveMap);
    public static string ToBold(string text) => MapText(text, BoldMap);
    public static string ToOldEnglish(string text) => MapText(text, FrakturMap);

    // ---- ptt_pynput.py's emoji pipeline, ported for voice-transcript styling ----

    // Fitzpatrick type-6 (dark) skin tone modifier -- matches the Linux build's
    // _SKIN_TONE, applied to every hand/person emoji in the keyword map.
    private const string SkinTone = "\U0001F3FF";

    // ptt_pynput.py's _TONEABLE_BASES: emoji codepoints that accept the modifier.
    private static readonly HashSet<string> ToneableBases = new()
    {
        "\U0001F44B", // waving hand
        "\u270C",     // victory hand
        "\U0001F64C", // raising hands
        "\U0001F919", // call me hand
        "\U0001F64F", // folded hands
        "\U0001F647", // person bowing
        "\U0001F44D", // thumbs up
        "\U0001F44E", // thumbs down
        "\U0001F44C", // OK hand
        "\U0001F937", // shrug
    };

    private static string ApplySkinTone(string emoji)
    {
        // Iterate by CODEPOINT (surrogate pairs together), matching Python's
        // char-level loop. A UTF-16 char-by-char loop corrupts astral-plane
        // emojis: appending the tone after a high surrogate orphans the low
        // surrogate and produces invalid UTF-8.
        var sb = new StringBuilder(emoji.Length + 4);
        var i = 0;
        while (i < emoji.Length)
        {
            var ch = emoji[i];
            var isHighSurrogate = char.IsHighSurrogate(ch);
            var codepoint = isHighSurrogate
                ? char.ConvertToUtf32(emoji, i)
                : ch;
            var cpStr = char.ConvertFromUtf32(codepoint);
            if (ToneableBases.Contains(cpStr))
            {
                sb.Append(cpStr).Append(SkinTone);
                i += isHighSurrogate ? 2 : 1;
                // Keep any emoji-variation selector after the tone.
                if (i < emoji.Length && emoji[i] == '\uFE0F')
                {
                    sb.Append('\uFE0F');
                    i++;
                }
                continue;
            }
            sb.Append(ch);
            if (isHighSurrogate)
            {
                sb.Append(emoji[i + 1]); // the low surrogate of the pair
                i++;
            }
            i++;
        }
        return sb.ToString();
    }

    private static readonly Dictionary<string, string> EmojiMap = BuildTonedEmojiMap();

    // ptt_pynput.py applies _apply_skin_tone to every value at import time
    // (_EMOJI_MAP = {k: _apply_skin_tone(v) for ...}); the static ctor below
    // does the same here, so every hand/person emoji carries the dark tone.
    private static Dictionary<string, string> BuildTonedEmojiMap()
    {
        var raw = new Dictionary<string, string>
        {
            // emotions
            ["happy"] = "happy 😊", ["sad"] = "sad 😢", ["love"] = "love ❤️", ["hate"] = "hate 😠",
            ["heart"] = "heart ❤️", ["excited"] = "excited 🤩", ["bored"] = "bored 😐",
            ["angry"] = "angry 😠", ["mad"] = "mad 🤬", ["tired"] = "tired 😴", ["sleepy"] = "sleepy 😴",
            ["sick"] = "sick 🤒", ["surprised"] = "surprised 😲", ["shocked"] = "shocked 😱",
            ["confused"] = "confused 😕", ["worried"] = "worried 😟", ["proud"] = "proud 🥹",
            ["embarrassed"] = "embarrassed 😳", ["scared"] = "scared 😨", ["lonely"] = "lonely 🥺",
            // reactions
            ["lol"] = "lol 😂", ["haha"] = "haha 😂", ["lmao"] = "lmao 🤣", ["wow"] = "wow 🤯",
            ["omg"] = "omg 😱", ["yay"] = "yay 🎉", ["woo"] = "woo 🥳", ["yikes"] = "yikes 😬",
            ["ugh"] = "ugh 😩", ["meh"] = "meh 😒", ["hm"] = "hm 🤔", ["hmm"] = "hmm 🤔",
            // greetings / goodbyes
            ["hello"] = "hello 👋🏿", ["hi"] = "hi 👋🏿", ["hey"] = "hey 👋🏿",
            ["goodbye"] = "goodbye 👋🏿", ["bye"] = "bye 👋🏿", ["see you"] = "see you 👋🏿",
            ["good morning"] = "good morning 🌅", ["good night"] = "good night 🌙",
            ["thank you"] = "thank you 🙏🏿", ["thanks"] = "thanks 🙏🏿", ["please"] = "please 🥺",
            ["sorry"] = "sorry 😔", ["apologize"] = "apologize 🙇🏿",
            // quality
            ["fire"] = "fire 🔥", ["cool"] = "cool 😎", ["nice"] = "nice ✨", ["great"] = "great 🎉",
            ["awesome"] = "awesome 🤩", ["amazing"] = "amazing 🤩", ["perfect"] = "perfect 💯",
            ["good"] = "good 👍🏿", ["bad"] = "bad 👎🏿", ["ok"] = "ok 👌🏿", ["okay"] = "okay 👌🏿",
            ["yes"] = "yes ✅", ["no"] = "no ❌", ["maybe"] = "maybe 🤷🏿", ["definitely"] = "definitely 💯",
            ["check"] = "check ✅", ["done"] = "done ✅", ["finished"] = "finished ✅",
            // food / drink
            ["hungry"] = "hungry 🍔", ["coffee"] = "coffee ☕", ["beer"] = "beer 🍺", ["wine"] = "wine 🍷",
            ["pizza"] = "pizza 🍕", ["taco"] = "taco 🌮", ["burger"] = "burger 🍔", ["fries"] = "fries 🍟",
            ["cake"] = "cake 🍰", ["ice cream"] = "ice cream 🍦", ["chocolate"] = "chocolate 🍫",
            ["water"] = "water 💧", ["tea"] = "tea 🍵", ["breakfast"] = "breakfast 🍳", ["dinner"] = "dinner 🍽️",
            // objects / tech
            ["phone"] = "phone 📱", ["computer"] = "computer 💻", ["laptop"] = "laptop 💻",
            ["game"] = "game 🎮", ["controller"] = "controller 🎮", ["music"] = "music 🎵",
            ["book"] = "book 📚", ["movie"] = "movie 🎬", ["tv"] = "tv 📺", ["money"] = "money 💰",
            ["idea"] = "idea 💡", ["light"] = "light 💡", ["warning"] = "warning ⚠️", ["rocket"] = "rocket 🚀",
            ["time"] = "time ⏰", ["date"] = "date 📅", ["mail"] = "mail 📧", ["email"] = "email 📧",
            // nature / animals
            ["sun"] = "sun ☀️", ["moon"] = "moon 🌙", ["star"] = "star ⭐", ["rain"] = "rain 🌧️",
            ["snow"] = "snow ❄️", ["ghost"] = "ghost 👻", ["skull"] = "skull 💀",
            ["cat"] = "cat 🐱", ["dog"] = "dog 🐶", ["bird"] = "bird 🐦", ["fish"] = "fish 🐟",
            // events
            ["party"] = "party 🎉", ["birthday"] = "birthday 🎂", ["congratulations"] = "congratulations 🎉",
            ["weekend"] = "weekend 🎉", ["work"] = "work 💼", ["job"] = "job 💼",
        };

        return raw.ToDictionary(kv => kv.Key, kv => ApplySkinTone(kv.Value));
    }

    private static readonly string[] CasualEmojis = BuildTonedCasualEmojis();

    // Linux: _CASUAL_EMOJIS = [_apply_skin_tone(e) for e in _CASUAL_EMOJIS]
    private static string[] BuildTonedCasualEmojis()
    {
        var raw = new[]
        {
        "\U0001F44B\U0001F3FF", // 👋🏿
        "☕", "😊", "\u270C\uFE0F\U0001F3FF", "\U0001F64C\U0001F3FF", "\U0001F919\U0001F3FF",
        "😎", "✨", "\U0001F4AF", "\U0001F525", "\U0001FAE1", // 💯 🔥 🫡
        };
        return raw.Select(ApplySkinTone).ToArray();
    }

    private static string AddKeywordEmoji(string text)
    {
        var lowered = text.ToLowerInvariant();
        // Longer phrases first so 'thank you' beats 'thanks' -- same as Linux.
        foreach (var phrase in EmojiMap.Keys.OrderByDescending(k => k.Length))
        {
            if (lowered.Contains(phrase))
            {
                var emoji = EmojiMap[phrase][phrase.Length..].Trim();
                return $"{text} {emoji}";
            }
        }
        return text;
    }

    private static string CasualEmojiBoost(string text)
    {
        // ptt_pynput.py: any(text.endswith(emoji) for emoji in _EMOJI_MAP.values())
        // -- the values are the FULL "keyword emoji" phrases, so the check only
        // suppresses the boost when the text truly ends with a keyword+emoji pair.
        var endsWithKeywordEmoji = EmojiMap.Values.Any(v => text.EndsWith(v, StringComparison.Ordinal));
        if (endsWithKeywordEmoji) return text;
        return $"{text} {CasualEmojis[Random.Shared.Next(CasualEmojis.Length)]}";
    }

    /// <summary>Apply a style by mode -- the same dispatch table as ptt_pynput.py's
    /// _transform_text for these five modes. PRO is passthrough; every other
    /// mode gets keyword-emoji insertion first, then its font/emoji transform.</summary>
    public static string Apply(string text, TextStyleMode mode)
    {
        if (mode == TextStyleMode.Pro) return text;
        text = AddKeywordEmoji(text);
        return mode switch
        {
            TextStyleMode.Bubbly => ToCursive(text),
            TextStyleMode.Casual => CasualEmojiBoost(text.ToLowerInvariant()),
            TextStyleMode.Bold => ToBold(text),
            TextStyleMode.Big => ToOldEnglish(text),
            _ => text,
        };
    }
}
