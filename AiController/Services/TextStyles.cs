using System.Text;

namespace AiController.Services;

/// <summary>The five transcript/typing styles, exactly matching the Linux build's
/// scripts/text_styles.py font maps and scripts/ptt_pynput.py's mode dispatch
/// (_transform_text). Same five names, same semantics: PRO is unchanged passthrough,
/// BUBBLY is cursive Unicode, CASUAL lowercases, BOLD and BIG map to bold-mathematical
/// and bold-Fraktur ("Old English") Unicode respectively.</summary>
public enum TextStyleMode { Pro, Bubbly, Casual, Bold, Big }

/// <summary>
/// Unicode font-mapping helpers, ported field-for-field from text_styles.py's
/// _CURSIVE_MAP/_BOLD_MAP/_FRAKTUR_MAP. Emoji-keyword insertion and skin-tone
/// modifiers from ptt_pynput.py's fuller pipeline are deliberately not ported --
/// out of scope for on-screen-keyboard mode chips, which only need the font
/// transform, not the STT post-processing pipeline.
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

    /// <summary>Apply a style by mode -- the same dispatch table as ptt_pynput.py's
    /// _transform_text for these five modes.</summary>
    public static string Apply(string text, TextStyleMode mode) => mode switch
    {
        TextStyleMode.Pro => text,
        TextStyleMode.Bubbly => ToCursive(text),
        TextStyleMode.Casual => text.ToLowerInvariant(),
        TextStyleMode.Bold => ToBold(text),
        TextStyleMode.Big => ToOldEnglish(text),
        _ => text,
    };
}
