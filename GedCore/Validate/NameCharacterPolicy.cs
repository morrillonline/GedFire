using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GedCore.Validate;

/// <summary>
/// Which characters a NAME value may contain. Letters of any script, combining marks, whitespace,
/// hyphens, apostrophes, periods, the middle dots of Catalan and Japanese, and the zero-width
/// joiners of Persian and Indic scripts are allowed; so are ordinal suffixes such as 3rd and, in the
/// suffix after the surname slashes, a comma as in "Smith/, Jr." Everything else, digits included, is disallowed.
/// </summary>
public static class NameCharacterPolicy
{
    // Typographic variants arrive by copy-paste: the Unicode hyphen, non-breaking hyphen, en dash and em dash.
    const string HyphenCharacters = "-\u2010\u2011\u2013\u2014";
    const string ApostropheCharacters = "'\u2019";

    // Period: initials and suffixes (H., Jr.). U+00B7: Catalan l·l (Cal·la). U+30FB: separator in a
    // Japanese rendering of a foreign name. U+200C/U+200D: zero-width joiners that Persian and Indic scripts need.
    const string OtherAllowedCharacters = ".\u00B7\u30FB\u200C\u200D";

    // The one place digits are allowed: an ordinal suffix such as 3rd, common in older files as "John /Smith/ 3rd".
    // The digits must be followed by st, nd, rd or th and stand alone, so "3" and "3rds" are still disallowed.
    static readonly Regex OrdinalToken = new(@"(?<![\p{L}\p{N}])\d+(?:st|nd|rd|th)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase);

    /// <summary>The distinct disallowed characters in <paramref name="nameValue"/>, in order of appearance.</summary>
    public static IReadOnlyList<string> DisallowedCharacters(string nameValue)
    {
        string text = OrdinalToken.Replace(nameValue, "");

        // A comma is allowed only after the closing surname slash, for "/Smith/, Jr."; "Smith, John" is inverted
        // and disallowed. The suffix cannot be told from an inverted given name by position, so "/Smith/, John" passes.
        int suffixStart = text.Contains('/') ? text.LastIndexOf('/') + 1 : int.MaxValue;

        var found = new List<string>();
        int offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bool allowed = rune.Value == '/' || IsAllowed(rune) || (rune.Value == ',' && offset >= suffixStart);
            if (!allowed && !found.Contains(rune.ToString())) found.Add(rune.ToString());
            offset += rune.Utf16SequenceLength;
        }
        return found;
    }

    /// <summary>
    /// True when the name uses punctuation as a placeholder: two or more hyphens in a row, or a part
    /// made only of hyphens, periods and apostrophes. Unknown is the preferred placeholder.
    /// </summary>
    public static bool HasPlaceholderPunctuation(string nameValue)
    {
        string text = new([.. nameValue.Select(c => HyphenCharacters.Contains(c) ? '-' : c)]);
        if (text.Contains("--", StringComparison.Ordinal)) return true;

        // A lone "-", "." or "'" as a whole part is a placeholder too, though each is allowed inside a name.

        return text.Split([' ', '\t', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.All(c => c is '-' or '.' || ApostropheCharacters.Contains(c)));
    }

    static bool IsAllowed(Rune rune) =>
        Rune.IsWhiteSpace(rune) || Rune.IsLetter(rune) || IsCombiningMark(rune) ||
        HyphenCharacters.Contains(rune.ToString()) || ApostropheCharacters.Contains(rune.ToString()) ||
        OtherAllowedCharacters.Contains(rune.ToString());

    static bool IsCombiningMark(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
}
