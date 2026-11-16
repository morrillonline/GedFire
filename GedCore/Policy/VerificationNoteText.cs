using System.Text.RegularExpressions;

namespace GedCore.Policy;

/// <summary>
/// The one test the drafter and the policy share for whether a note is a verification note naming a fact: it contains the
/// verification prefix and speaks of the fact by its word (see <see cref="FactLabel"/>) or a word for it.
/// </summary>
internal static class VerificationNoteText
{
    // The facts that had a word before every fact did; any other fact was named by its lowercased tag, as notes in trees still are.
    static readonly HashSet<string> FirstLabeled = new(StringComparer.Ordinal) { "BIRT", "DEAT", "BURI", "MARR", "CHR", "BAPM", "CENS", "RESI" };

    // Words an author's own note may use for a fact instead of its label.
    static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.Ordinal)
    {
        ["BIRT"] = ["born"], ["DEAT"] = ["died"], ["BURI"] = ["buried"], ["MARR"] = ["married"],
        ["CHR"] = ["christened"], ["BAPM"] = ["baptized", "baptised"], ["CENS"] = ["census", "enumerated"], ["RESI"] = ["resided", "lived"],
    };

    public static IReadOnlyDictionary<string, string> Labels => FactLabel.All;

    public static string Label(string tag) => FactLabel.Of(tag);

    /// <summary>The tag a note's label names: a fact's word, or the lowercased tag an older note used for a fact that had no word.</summary>
    public static string? TagOf(string label) =>
        Labels.FirstOrDefault(l => l.Value == label).Key
        ?? Labels.Keys.FirstOrDefault(tag => !FirstLabeled.Contains(tag) && tag.ToLowerInvariant() == label);

    /// <summary>Whether <paramref name="text"/> is a verification note naming <paramref name="tag"/>; any verification note when the tag is unknown.</summary>
    public static bool Names(string text, string prefix, string? tag) =>
        text.Contains(prefix, StringComparison.OrdinalIgnoreCase) && (tag is null || Mentions(text, tag));

    /// <summary>Whether <paramref name="text"/> speaks of the fact <paramref name="tag"/>, by its word or another word for it.</summary>
    /// <remarks>A word inside another fact's longer word does not count: "marriage banns" is not the marriage.</remarks>
    public static bool Mentions(string text, string tag) => WordsFor(tag)
        .Any(word => Regex.IsMatch(WithoutLongerWords(text, word, tag), Bounded(word), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    static string WithoutLongerWords(string text, string word, string tag) =>
        Labels.Where(l => l.Key != tag && l.Value.Length > word.Length && Regex.IsMatch(l.Value, Bounded(word), RegexOptions.IgnoreCase))
            .Aggregate(text, (remaining, longer) => Regex.Replace(remaining, Bounded(longer.Value), " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    static string Bounded(string word) => $@"\b{Regex.Escape(word)}\b";

    static IEnumerable<string> WordsFor(string tag)
    {
        yield return Label(tag);
        foreach (string synonym in Synonyms.GetValueOrDefault(tag, [])) yield return synonym;
        if (!FirstLabeled.Contains(tag)) yield return tag.ToLowerInvariant();
    }
}
