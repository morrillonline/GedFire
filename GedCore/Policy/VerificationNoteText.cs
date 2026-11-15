using System.Text.RegularExpressions;

namespace GedCore.Policy;

/// <summary>
/// The one test the drafter and the policy share for whether a note is a verification note naming a fact: it contains the
/// verification prefix and speaks of the fact by its label or a word for it.
/// </summary>
internal static class VerificationNoteText
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BIRT"] = "birth", ["DEAT"] = "death", ["BURI"] = "burial", ["MARR"] = "marriage",
        ["CHR"] = "christening", ["BAPM"] = "baptism", ["CENS"] = "census entry", ["RESI"] = "residence",
    };

    // Words an author's own note may use for a fact instead of its label.
    static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.Ordinal)
    {
        ["BIRT"] = ["born"], ["DEAT"] = ["died"], ["BURI"] = ["buried"], ["MARR"] = ["married"],
        ["CHR"] = ["christened"], ["BAPM"] = ["baptized", "baptised"], ["CENS"] = ["census", "enumerated"], ["RESI"] = ["resided", "lived"],
    };

    public static string Label(string tag) => Labels.GetValueOrDefault(tag, tag.ToLowerInvariant());

    /// <summary>Whether <paramref name="text"/> is a verification note naming <paramref name="tag"/>; any verification note when the tag is unknown.</summary>
    public static bool Names(string text, string prefix, string? tag) =>
        text.Contains(prefix, StringComparison.OrdinalIgnoreCase) && (tag is null || Mentions(text, tag));

    /// <summary>Whether <paramref name="text"/> speaks of the fact <paramref name="tag"/>, by its label or a word for it.</summary>
    public static bool Mentions(string text, string tag) =>
        new[] { Label(tag) }.Concat(Synonyms.GetValueOrDefault(tag, []))
            .Any(word => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
}
